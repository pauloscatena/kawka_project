using System.Diagnostics;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class TopicService : ITopicService
{
    private static readonly TimeSpan DeletionPropagationGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DeletionPollInterval     = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MetadataQueryTimeout     = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DeletionTimeout          = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WatermarkQueryTimeout    = TimeSpan.FromSeconds(5);

    /// <summary>How many consecutive polls must report the topic gone before we believe it.</summary>
    private const int RequiredConsecutiveAbsences = 2;

    // Internal, not private: the retry unit tests pass their own attempt count, so nothing else in
    // the suite would notice these being set to "do not retry".
    internal static readonly TimeSpan CreateRetryDelay = TimeSpan.FromSeconds(2);
    internal const int CreateAttempts = 3;

    /// <summary>
    /// Create failures that will fail identically on every retry. Measured: retrying an
    /// InvalidReplicationFactor takes 4.0s instead of 39ms, all of it Task.Delay, while the user
    /// watches a spinner with their topic already deleted.
    /// </summary>
    private static readonly ErrorCode[] PermanentCreateErrors =
    {
        ErrorCode.InvalidReplicationFactor,
        ErrorCode.InvalidPartitions,
        ErrorCode.InvalidConfig,
        ErrorCode.InvalidReplicaAssignment,
        ErrorCode.PolicyViolation
    };

    private static AdminClientConfig AdminConfig(IKafkaSession session)
    {
        var cfg = new AdminClientConfig();
        ((KafkaSession)session).ApplyTo(cfg);
        return cfg;
    }

    public async Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);
        return meta.Topics
            .Where(t => !t.Topic.StartsWith("__"))
            .Select(t => new TopicInfo(
                t.Topic,
                t.Partitions.Count,
                (short)t.Partitions[0].Replicas.Length));
    }

    public async Task<TopicDetail> GetTopicDetailAsync(IKafkaSession session, string topicName)
    {
        var adminCfg = AdminConfig(session);
        using var admin = new AdminClientBuilder(adminCfg).Build();

        // Full-cluster metadata, NOT GetMetadata(topicName, ...): the single-topic overload
        // auto-creates the topic when auto.create.topics.enable is on (the broker default), so
        // opening the detail view of a topic someone else just deleted would silently recreate it.
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);
        var topicMeta = meta.Topics.FirstOrDefault(t => t.Topic == topicName)
            ?? throw new InvalidOperationException($"Topic '{topicName}' was not found on the cluster.");

        var consumerCfg = new ConsumerConfig { GroupId = $"kawka-detail-{Guid.NewGuid()}" };
        ((KafkaSession)session).ApplyTo(consumerCfg);
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(consumerCfg).Build();

        // QueryWatermarkOffsets is a BLOCKING call with its own timeout, once per partition.
        // Without this Task.Run the loop runs on whatever thread the await resumed on - the
        // Avalonia UI thread - freezing the window for up to WatermarkQueryTimeout x partitionCount
        // when a broker is unreachable.
        var partitions = await Task.Run(() => topicMeta.Partitions.Select(p =>
        {
            var tp = new TopicPartition(topicName, new Partition(p.PartitionId));
            var wm = consumer.QueryWatermarkOffsets(tp, WatermarkQueryTimeout);
            return new PartitionInfo(p.PartitionId, p.Leader, wm.Low.Value, wm.High.Value);
        }).ToList()).ConfigureAwait(false);

        // NOTE, both left for Task 10 of the plan to keep this task's diff reviewable on its own:
        // deriving the replication factor from partition 0 is wrong for a topic with a non-uniform
        // assignment, and indexing Partitions[0] at all is unguarded - a topic that exists but
        // reports zero partitions would throw IndexOutOfRange here. GetPartitionCountAsync below
        // guards exactly that case; this method and ListTopicsAsync do not. Not reproducible on a
        // KRaft single-node broker, but the three should agree.
        var info = new TopicInfo(topicMeta.Topic, partitions.Count,
            (short)topicMeta.Partitions[0].Replicas.Length);
        return new TopicDetail(info, partitions);
    }

    public async Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification { Name = name, NumPartitions = partitionCount, ReplicationFactor = replicationFactor }
        }).ConfigureAwait(false);
    }

    public async Task DeleteTopicAsync(IKafkaSession session, string topicName)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.DeleteTopicsAsync(new[] { topicName }).ConfigureAwait(false);
    }

    public async Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.CreatePartitionsAsync(new[]
        {
            new PartitionsSpecification { Topic = topicName, IncreaseTo = newPartitionCount }
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetTopicConfigOverridesAsync(IKafkaSession session, string topicName)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var results = await admin.DescribeConfigsAsync(new[]
        {
            new ConfigResource { Type = ResourceType.Topic, Name = topicName }
        }).ConfigureAwait(false);
        // Filter on Source, never on IsDefault. IsDefault is NOT a value comparison - it means
        // "Source == DefaultConfig". An override set explicitly on the topic reports IsDefault
        // false even when its value happens to equal the default (measured: min.insync.replicas=1
        // set via CreateTopics reports DynamicTopicConfig / IsDefault=false).
        //
        // So !IsDefault lets through everything the topic merely INHERITS: StaticBrokerConfig
        // (server.properties) and DynamicBrokerConfig (kafka-configs --entity-type brokers).
        // Carrying those into the recreate writes them back as explicit topic-level overrides,
        // freezing that topic against every future cluster-wide change.
        // Only DynamicTopicConfig means "somebody set this on this topic".
        return results[0].Entries.Values
            .Where(e => e.Source == ConfigSource.DynamicTopicConfig)
            .ToDictionary(e => e.Name, e => e.Value);
    }

    public async Task RecreateTopicWithFewerPartitionsAsync(
        IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();

        // Validate BEFORE anything destructive: once DeleteTopicsAsync is issued the deletion is
        // asynchronous and irrevocable, so an invalid argument discovered afterwards costs the
        // user their data. An operation that cannot be undone must check its own arguments
        // rather than trusting whoever happens to call it.
        var currentCount = await GetPartitionCountAsync(admin, topicName).ConfigureAwait(false);

        // Handled before the range check: with currentCount == 1 the valid range is empty, and the
        // range message would read "must be between 1 and 0". This is a fact about the topic, not
        // about the argument, so it is not an ArgumentException.
        if (currentCount <= 1)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' has a single partition; there is nothing to reduce.");
        }

        if (newPartitionCount < 1 || newPartitionCount >= currentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newPartitionCount), newPartitionCount,
                $"Must be between 1 and {currentCount - 1}: topic '{topicName}' currently has " +
                $"{currentCount} partitions, and this operation only reduces the partition count.");
        }

        // The config read is the last non-destructive step. Everything after it is wrapped so the
        // failure carries WHERE it happened, whether the data is genuinely at risk, and what the
        // topic was made of - the caller cannot re-derive any of it once the topic is gone.
        IReadOnlyDictionary<string, string> config;
        try
        {
            config = await GetTopicConfigOverridesAsync(session, topicName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TopicRecreateFailedException(
                TopicRecreateStage.ReadingConfig, topicMayBeDeleted: false,
                new TopicRecreateAttempt(topicName, currentCount, newPartitionCount, replicationFactor,
                    new Dictionary<string, string>()),
                $"Could not read the configuration of topic '{topicName}': {ex.Message}. It was NOT modified.",
                ex);
        }

        var attempt = new TopicRecreateAttempt(
            topicName, currentCount, newPartitionCount, replicationFactor, config);

        await RunRecreateStagesAsync(
            attempt,
            deleteTopic: () => admin.DeleteTopicsAsync(new[] { topicName }),
            waitForDeletion: () => WaitForTopicDeletionAsync(admin, topicName),
            createWithRetry: () => CreateWithRetryAsync(
                attempt,
                () => admin.CreateTopicsAsync(new[] { SpecFor(attempt) }),
                () => TopicMatchesRequestAsync(admin, attempt),
                CreateAttempts, CreateRetryDelay)).ConfigureAwait(false);
    }

    private static TopicSpecification SpecFor(TopicRecreateAttempt attempt) => new()
    {
        Name = attempt.TopicName,
        NumPartitions = attempt.RequestedPartitionCount,
        ReplicationFactor = attempt.ReplicationFactor,
        Configs = new Dictionary<string, string>(attempt.PreservedConfig)
    };

    /// <summary>
    /// Internal so unit tests can script each stage failing. Every one of these translations is a
    /// safety statement about the user's data, and none of them are observable through a healthy
    /// broker - the integration suite stays green even if the stages are mislabelled.
    /// </summary>
    internal static async Task RunRecreateStagesAsync(
        TopicRecreateAttempt attempt,
        Func<Task> deleteTopic,
        Func<Task> waitForDeletion,
        Func<Task> createWithRetry)
    {
        try
        {
            await deleteTopic().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A per-topic error code from the BROKER means the request reached the controller and
            // came back refused - an ACL denial, or delete.topic.enable=false - which is precisely
            // why it was not executed. The topic is intact. Only a local failure (timeout,
            // transport) leaves the outcome genuinely unknown.
            var refusedByBroker = ex is DeleteTopicsException deleteFailure
                && deleteFailure.Results.Any(r => r.Error.IsError && !r.Error.IsLocalError);

            throw new TopicRecreateFailedException(
                TopicRecreateStage.Deleting, topicMayBeDeleted: !refusedByBroker, attempt,
                refusedByBroker
                    ? $"The cluster refused to delete topic '{attempt.TopicName}': {ex.Message}. It was NOT modified."
                    : $"The delete request for topic '{attempt.TopicName}' failed and may or may not have "
                      + $"reached the controller: {ex.Message}",
                ex);
        }

        try
        {
            await waitForDeletion().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TopicRecreateFailedException(
                TopicRecreateStage.WaitingForDeletion, topicMayBeDeleted: true, attempt,
                $"Deletion of topic '{attempt.TopicName}' was accepted but could not be confirmed in "
                + $"time, so it was not recreated: {ex.Message}", ex);
        }

        await createWithRetry().ConfigureAwait(false);
    }

    /// <summary>
    /// Internal so a unit test can drive the retry with scripted failures and no delay. A create
    /// that fails transiently and then succeeds is exactly what a single-node container cannot be
    /// made to produce on demand.
    /// </summary>
    internal static async Task CreateWithRetryAsync(
        TopicRecreateAttempt attempt,
        Func<Task> createTopic,
        Func<Task<bool>> createdTopicMatchesRequest,
        int attempts,
        TimeSpan retryDelay)
    {
        // The delete already happened, so this is the one call worth fighting for: every failure
        // here leaves the user without their topic.
        Exception? lastError = null;

        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                await createTopic().ConfigureAwait(false);
                return;
            }
            catch (CreateTopicsException ex) when (IsNameTaken(ex))
            {
                // Something occupies the name. Do NOT guess whether it was our own request landing
                // with a lost response: ask the cluster whether what is there is what we asked for.
                // A heuristic here reports a shrink that never happened, with the data already gone.
                if (await createdTopicMatchesRequest().ConfigureAwait(false)) return;

                throw new TopicRecreateFailedException(
                    TopicRecreateStage.Creating, topicMayBeDeleted: true, attempt,
                    $"Topic '{attempt.TopicName}' was deleted, and a topic with that name exists again "
                    + $"but is not the one requested ({attempt.RequestedPartitionCount} partitions). "
                    + "Something else recreated it; the requested change was NOT applied.", ex);
            }
            catch (Exception ex)
            {
                lastError = ex;

                // Configuration errors are deterministic. Retrying spends the budget - and the
                // user's patience, with their topic already deleted - to fail identically.
                if (IsPermanentCreateFailure(ex)) break;
                if (i < attempts) await Task.Delay(retryDelay).ConfigureAwait(false);
            }
        }

        throw new TopicRecreateFailedException(
            TopicRecreateStage.Creating, topicMayBeDeleted: true, attempt,
            $"Topic '{attempt.TopicName}' was deleted but could not be recreated: {lastError!.Message}",
            lastError);
    }

    private static bool IsNameTaken(CreateTopicsException ex) =>
        ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists);

    internal static bool IsPermanentCreateFailure(Exception ex) =>
        ex is CreateTopicsException create
        && create.Results.Any(r => PermanentCreateErrors.Contains(r.Error.Code));

    private static async Task<bool> TopicMatchesRequestAsync(IAdminClient admin, TopicRecreateAttempt attempt)
    {
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);
        var topic = meta.Topics.FirstOrDefault(t => t.Topic == attempt.TopicName);

        return topic is not null
               && !topic.Error.IsError
               && topic.Partitions.Count == attempt.RequestedPartitionCount;
    }

    private static async Task<int> GetPartitionCountAsync(IAdminClient admin, string topicName)
    {
        // Full-cluster metadata, NOT GetMetadata(topicName, ...): asking a broker about one named
        // topic auto-creates it when auto.create.topics.enable is on (the default). That would turn
        // "recreate a topic whose name I typo'd" into "silently create a topic", and would make the
        // not-found check below unreachable.
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);
        var topic = meta.Topics.FirstOrDefault(t => t.Topic == topicName);

        if (topic is null || topic.Error.Code == ErrorCode.UnknownTopicOrPart)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' was not found on the cluster; refusing to recreate it.");
        }

        // A topic that exists can still answer with a transient error (LeaderNotAvailable during an
        // election, for instance). Reporting that as "not found" would be a lie; both refuse, but
        // only one of them tells the operator what to actually go and look at.
        if (topic.Error.IsError)
        {
            throw new InvalidOperationException(
                $"Could not read metadata for topic '{topicName}': {topic.Error.Reason}. " +
                "Refusing to recreate it until the cluster answers reliably.");
        }

        if (topic.Partitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' reported no partitions; refusing to recreate it.");
        }

        return topic.Partitions.Count;
    }

    private static Task WaitForTopicDeletionAsync(IAdminClient admin, string topicName) =>
        WaitForTopicDeletionAsync(
            topicName,
            () => TopicIsAbsentAsync(admin, topicName),
            DeletionPropagationGrace, DeletionPollInterval, DeletionTimeout);

    /// <summary>
    /// Internal so a unit test can drive the loop with a scripted sequence of answers and zero
    /// delays. Measuring this from the outside does not work: a full recreate spends hundreds of
    /// milliseconds building librdkafka clients, which swamps the poll intervals being asserted on.
    /// </summary>
    internal static async Task WaitForTopicDeletionAsync(
        string topicName,
        Func<Task<bool>> topicIsAbsent,
        TimeSpan grace,
        TimeSpan pollInterval,
        TimeSpan timeout)
    {
        // DeleteTopicsAsync returns as soon as the controller ACCEPTS the request; brokers learn
        // about it asynchronously via UpdateMetadata. Polling immediately would just read
        // pre-deletion metadata, so give propagation a head start before the first poll.
        await Task.Delay(grace).ConfigureAwait(false);

        // Stopwatch, not DateTime.UtcNow: the latter is not monotonic, so an NTP step forward
        // during the loop would end the budget early - reporting a timeout for a deletion that is
        // still progressing - and a step backward would extend it indefinitely.
        var elapsed = Stopwatch.StartNew();
        Exception? lastException = null;
        var consecutiveAbsences = 0;

        while (elapsed.Elapsed < timeout)
        {
            try
            {
                // Require the topic to be missing on two CONSECUTIVE polls before believing it.
                //
                // Be honest about what this buys: the Kafka metadata protocol carries no
                // completeness flag, so a partial response from a broker that has not yet received
                // UpdateMetadata is indistinguishable from a complete one. There is no signal to
                // assert on. Two samples one poll apart is a probability reduction, not a
                // guarantee - it only rules out a degenerate window shorter than
                // DeletionPollInterval. Cheap insurance on a step that cannot be undone.
                if (await topicIsAbsent().ConfigureAwait(false))
                {
                    if (++consecutiveAbsences >= RequiredConsecutiveAbsences) return;
                }
                else
                {
                    consecutiveAbsences = 0;
                }

                // This poll answered. Do not let an older, unrelated error go on to be reported as
                // the cause of a timeout whose real cause is "the deletion never propagated".
                lastException = null;
            }
            catch (Exception ex)
            {
                lastException = ex;
                consecutiveAbsences = 0;
            }

            await Task.Delay(pollInterval).ConfigureAwait(false);
        }

        // The budget can expire holding exactly one observed absence - propagation completing just
        // before the deadline. Confirm once more instead of reporting a timeout for a deletion that
        // actually finished: downstream, that timeout becomes a data-loss warning shown to a user
        // whose topic is fine.
        if (consecutiveAbsences >= 1)
        {
            try
            {
                if (await topicIsAbsent().ConfigureAwait(false)) return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        // Budget note: a slow broker can burn the full MetadataQueryTimeout per attempt, so the
        // worst case is ~3 polls inside DeletionTimeout, not the ~60 that "poll every 500ms for
        // 30s" suggests at a glance.
        var detail = lastException is not null
            ? $" Last metadata error: {lastException.Message}"
            : " Metadata queries succeeded, but the cluster kept reporting the topic or kept " +
              "answering with incomplete metadata.";

        throw new TimeoutException(
            $"Timed out after {timeout.TotalSeconds:0}s waiting for topic '{topicName}' to disappear " +
            $"from cluster metadata.{detail}",
            lastException);
    }

    private static async Task<bool> TopicIsAbsentAsync(IAdminClient admin, string topicName)
    {
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);
        return meta.Topics.All(t => t.Topic != topicName);
    }
}
