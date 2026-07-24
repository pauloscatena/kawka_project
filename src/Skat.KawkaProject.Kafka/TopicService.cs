using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class TopicService : ITopicService
{
    private static AdminClientConfig AdminConfig(IKafkaSession session)
    {
        var cfg = new AdminClientConfig();
        ((KafkaSession)session).ApplyTo(cfg);
        return cfg;
    }

    public async Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var meta = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
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
        var meta = await Task.Run(() => admin.GetMetadata(topicName, TimeSpan.FromSeconds(10)));
        var topicMeta = meta.Topics.First();

        var consumerCfg = new ConsumerConfig { GroupId = $"kawka-detail-{Guid.NewGuid()}" };
        ((KafkaSession)session).ApplyTo(consumerCfg);
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(consumerCfg).Build();

        var partitions = topicMeta.Partitions.Select(p =>
        {
            var tp = new TopicPartition(topicName, new Partition(p.PartitionId));
            var wm = consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5));
            return new PartitionInfo(p.PartitionId, p.Leader, wm.Low.Value, wm.High.Value);
        }).ToList();

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
        });
    }

    public async Task DeleteTopicAsync(IKafkaSession session, string topicName)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.DeleteTopicsAsync(new[] { topicName });
    }

    public async Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.CreatePartitionsAsync(new[]
        {
            new PartitionsSpecification { Topic = topicName, IncreaseTo = newPartitionCount }
        });
    }

    public async Task<IReadOnlyDictionary<string, string>> GetTopicConfigOverridesAsync(IKafkaSession session, string topicName)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var results = await admin.DescribeConfigsAsync(new[]
        {
            new ConfigResource { Type = ResourceType.Topic, Name = topicName }
        });
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
        var currentCount = await GetPartitionCountAsync(admin, topicName);

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

        var config = await GetTopicConfigOverridesAsync(session, topicName);

        await admin.DeleteTopicsAsync(new[] { topicName });
        await WaitForTopicDeletionAsync(admin, topicName);

        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = topicName,
                NumPartitions = newPartitionCount,
                ReplicationFactor = replicationFactor,
                Configs = new Dictionary<string, string>(config)
            }
        });
    }

    private static async Task<int> GetPartitionCountAsync(IAdminClient admin, string topicName)
    {
        // Full-cluster metadata, NOT GetMetadata(topicName, ...): asking a broker about one named
        // topic auto-creates it when auto.create.topics.enable is on (the default). That would turn
        // "recreate a topic whose name I typo'd" into "silently create a topic", and would make the
        // not-found check below unreachable.
        var meta = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
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

    private static async Task WaitForTopicDeletionAsync(IAdminClient admin, string topicName)
    {
        await Task.Delay(500); // Initial delay to allow deletion to start
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastException = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var meta = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
                if (!meta.Topics.Any(t => t.Topic == topicName)) return;
            }
            catch (Exception ex)
            {
                // Metadata query might fail temporarily during deletion, retry
                lastException = ex;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Timed out waiting for topic '{topicName}' deletion before recreate.", lastException);
    }
}
