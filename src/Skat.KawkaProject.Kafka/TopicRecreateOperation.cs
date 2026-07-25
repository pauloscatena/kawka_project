using System.Diagnostics;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Exceptions;

namespace Skat.KawkaProject.Kafka;

/// <summary>
/// The delete-and-recreate saga: validate, read config, delete, wait for propagation, recreate
/// with retry. It lives in its own type because it is a multi-step operation with an irreversible
/// midpoint - a different animal from the one-call-per-method adapter that is <see cref="TopicService"/>.
/// Keeping it here lets TopicService stay a thin AdminClient wrapper, and keeps the stage
/// translations (each a safety statement about the user's data) together where they can be
/// unit-tested through the internal seams.
/// </summary>
internal static class TopicRecreateOperation
{
    private static readonly TimeSpan DeletionPropagationGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DeletionPollInterval     = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DeletionTimeout          = TimeSpan.FromSeconds(30);

    /// <summary>How many consecutive polls must report the topic gone before we believe it.</summary>
    private const int RequiredConsecutiveAbsences = 2;

    /// <summary>
    /// Gap between the two metadata reads that must agree before the topic's shape is trusted. Same
    /// reasoning as <see cref="RequiredConsecutiveAbsences"/>, applied to the other direction: back
    /// to back reads would come off one warm cache and agree for the wrong reason.
    /// </summary>
    private static readonly TimeSpan MetadataConfirmationInterval = TimeSpan.FromMilliseconds(500);

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

    /// <summary>
    /// Runs the whole saga against an already-built admin client. Config reading is passed in as a
    /// delegate rather than reached for directly: that is an adapter concern (TopicService owns it),
    /// and keeping it out of here is what lets this type not depend on TopicService.
    /// </summary>
    public static async Task ExecuteAsync(
        IAdminClient admin,
        Func<Task<IReadOnlyDictionary<string, string>>> readConfigOverrides,
        string topicName, int newPartitionCount)
    {
        // Validate BEFORE anything destructive: once DeleteTopicsAsync is issued the deletion is
        // asynchronous and irrevocable, so an invalid argument discovered afterwards costs the
        // user their data. An operation that cannot be undone must check its own arguments
        // rather than trusting whoever happens to call it.
        //
        // The replication factor is derived here from the live topic, not taken from the caller: a
        // caller's value is a snapshot that can be stale by the time the recreate runs (a completed
        // reassignment), and rebuilding the topic from a stale factor changes durability silently on
        // a destructive path.
        var (currentCount, replicationFactor) = await GetTopicFactsAsync(admin, topicName).ConfigureAwait(false);

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
            config = await readConfigOverrides().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TopicRecreateFailedException(
                TopicRecreateStage.ReadingConfig, topicMayBeDeleted: false,
                new TopicRecreateAttempt(topicName, currentCount, newPartitionCount, replicationFactor,
                    new Dictionary<string, string>()),
                $"Could not read the configuration of topic '{topicName}': {AsSentence(ex.Message)} It was NOT modified.",
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
                    ? $"The cluster refused to delete topic '{attempt.TopicName}': {AsSentence(ex.Message)} It was NOT modified."
                    : $"The delete request for topic '{attempt.TopicName}' failed and may or may not have "
                      + $"reached the controller: {AsSentence(ex.Message)}",
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
                + $"time, so it was not recreated: {AsSentence(ex.Message)}", ex);
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
                bool matchesRequest;
                try
                {
                    matchesRequest = await createdTopicMatchesRequest().ConfigureAwait(false);
                }
                catch (Exception probeFailure)
                {
                    // An exception thrown inside a catch block is NOT caught by that try's sibling
                    // catches. Without this, one network blip - the same one that lost the create
                    // response - escapes untyped all the way to the caller, which has no way to know
                    // a delete ever happened and reports it as a bare "Local: Timed out".
                    throw new TopicRecreateFailedException(
                        TopicRecreateStage.Creating, topicMayBeDeleted: true, attempt,
                        $"Topic '{attempt.TopicName}' was deleted and its name is taken again, but the "
                        + $"cluster could not be queried to find out by what: {AsSentence(probeFailure.Message)}",
                        probeFailure);
                }

                if (matchesRequest) return;

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
            $"Topic '{attempt.TopicName}' was deleted but could not be recreated: {AsSentence(lastError!.Message)}",
            lastError);
    }

    /// <summary>
    /// Broker messages sometimes end in a period and sometimes do not, so joining one to the next
    /// sentence blindly yields either "failed].. It was" or "Local: Timed out Check 'orders'".
    /// </summary>
    private static string AsSentence(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0) return "";
        return ".!?".Contains(trimmed[^1]) ? trimmed : trimmed + ".";
    }

    private static bool IsNameTaken(CreateTopicsException ex) =>
        ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists);

    internal static bool IsPermanentCreateFailure(Exception ex) =>
        ex is CreateTopicsException create
        && create.Results.Any(r => PermanentCreateErrors.Contains(r.Error.Code));

    private static async Task<bool> TopicMatchesRequestAsync(IAdminClient admin, TopicRecreateAttempt attempt)
    {
        var meta = await Task.Run(() => admin.GetMetadata(KafkaTimeouts.MetadataQueryTimeout)).ConfigureAwait(false);
        var topic = meta.Topics.FirstOrDefault(t => t.Topic == attempt.TopicName);

        return topic is not null
               && !topic.Error.IsError
               && topic.Partitions.Count == attempt.RequestedPartitionCount;
    }

    // Reads the metadata; the judgement about what it says lives in TopicMetadataFacts, where it can
    // be unit-tested. The states worth refusing here - a partition reporting an error, a replica
    // list the broker omitted, two reads disagreeing - cannot be produced by the healthy
    // single-broker container the integration suite runs against, so a guard verified only through
    // it is not verified.
    //
    // Two reads that must agree, for the same reason WaitForTopicDeletionAsync demands two
    // consecutive absences: Kafka's metadata protocol carries no completeness flag, so a partial
    // answer from a broker whose cache is still warming is indistinguishable from a complete one.
    // The delete path already refuses to act on a single sample; the shape the topic is rebuilt
    // with - and the partition count recorded for manual recovery - deserves the same.
    private static async Task<(int PartitionCount, short ReplicationFactor)> GetTopicFactsAsync(
        IAdminClient admin, string topicName)
    {
        var first = await ReadTopicFactsAsync(admin, topicName).ConfigureAwait(false);
        await Task.Delay(MetadataConfirmationInterval).ConfigureAwait(false);
        var second = await ReadTopicFactsAsync(admin, topicName).ConfigureAwait(false);

        return TopicMetadataFacts.Agreed(first, second, topicName);
    }

    private static async Task<(int PartitionCount, short ReplicationFactor)> ReadTopicFactsAsync(
        IAdminClient admin, string topicName)
    {
        // Full-cluster metadata, NOT GetMetadata(topicName, ...): asking a broker about one named
        // topic auto-creates it when auto.create.topics.enable is on (the default). That would turn
        // "recreate a topic whose name I typo'd" into "silently create a topic", and would make the
        // not-found check unreachable.
        var meta = await Task.Run(() => admin.GetMetadata(KafkaTimeouts.MetadataQueryTimeout)).ConfigureAwait(false);
        return TopicMetadataFacts.FactsFor(meta.Topics.FirstOrDefault(t => t.Topic == topicName), topicName);
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

        // Budget note: a slow broker can burn the full KafkaTimeouts.MetadataQueryTimeout per
        // attempt, so the worst case is ~3 polls inside DeletionTimeout, not the ~60 that "poll
        // every 500ms for 30s" suggests at a glance.
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
        var meta = await Task.Run(() => admin.GetMetadata(KafkaTimeouts.MetadataQueryTimeout)).ConfigureAwait(false);
        return meta.Topics.All(t => t.Topic != topicName);
    }
}
