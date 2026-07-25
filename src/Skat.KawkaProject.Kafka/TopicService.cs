using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class TopicService : ITopicService
{
    private static readonly TimeSpan MetadataQueryTimeout  = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WatermarkQueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Derives a topic's replication factor from its per-partition replica counts. Internal so the
    /// non-uniform case can be unit-tested; a single-broker container cannot produce one.
    /// </summary>
    internal static short ReplicationFactorOf(IEnumerable<int> replicaCountsPerPartition)
    {
        // Minimum, not the first partition's count. A non-uniform assignment (an interrupted
        // reassignment) would otherwise report partition 0's factor, and a recreate built from it
        // would ask for that factor uniformly - misrepresenting the topic's real durability. The
        // minimum tells the truth about the weakest partition. DefaultIfEmpty avoids the
        // IndexOutOfRange the old Partitions[0] indexing threw for a topic reporting no partitions.
        return (short)replicaCountsPerPartition.DefaultIfEmpty(0).Min();
    }

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
                ReplicationFactorOf(t.Partitions.Select(p => p.Replicas.Length))));
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

        var info = new TopicInfo(topicMeta.Topic, partitions.Count,
            ReplicationFactorOf(topicMeta.Partitions.Select(p => p.Replicas.Length)));
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

    // The delete-and-recreate saga - a multi-step operation with an irreversible midpoint - lives
    // in TopicRecreateOperation, not here: this class is a thin adapter (one AdminClient call per
    // method) and the saga is a different animal. This method just builds the client and hands the
    // config read (an adapter concern) to it as a delegate.
    public async Task DeleteAndRecreateTopicAsync(
        IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await TopicRecreateOperation.ExecuteAsync(
            admin,
            () => GetTopicConfigOverridesAsync(session, topicName),
            topicName, newPartitionCount, replicationFactor).ConfigureAwait(false);
    }
}
