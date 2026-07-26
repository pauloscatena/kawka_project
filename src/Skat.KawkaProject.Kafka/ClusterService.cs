using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class ClusterService : IClusterService
{
    private static AdminClientConfig AdminConfig(IKafkaSession session)
    {
        var cfg = new AdminClientConfig();
        ((KafkaSession)session).ApplyTo(cfg);
        return cfg;
    }

    public async Task<IEnumerable<BrokerInfo>> ListBrokersAsync(IKafkaSession session)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var meta = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
        return meta.Brokers.Select(b => new BrokerInfo(
            b.BrokerId, b.Host, b.Port,
            b.BrokerId == meta.OriginatingBrokerId));
    }

    public async Task<IEnumerable<ConsumerGroupInfo>> ListConsumerGroupsAsync(IKafkaSession session)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var result = await admin.ListConsumerGroupsAsync();
        return result.Valid.Select(g => new ConsumerGroupInfo(g.GroupId, g.State.ToString(), 0));
    }

    public async Task<IEnumerable<PartitionLag>> GetGroupLagAsync(IKafkaSession session, string groupId)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();

        var offsetsResult = await admin.ListConsumerGroupOffsetsAsync(
            new[] { new ConsumerGroupTopicPartitions(groupId, null) });
        var partitionOffsets = offsetsResult[0].Partitions;

        if (!partitionOffsets.Any()) return Enumerable.Empty<PartitionLag>();

        var consumerCfg = new ConsumerConfig { GroupId = $"kawka-lag-{Guid.NewGuid()}" };
        ((KafkaSession)session).ApplyTo(consumerCfg);
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(consumerCfg).Build();

        // ToList inside the using, not a deferred Select escaping it. Select is lazy, so the
        // watermark queries used to run when the caller enumerated - by which point `using` had
        // already destroyed the consumer's native handle, and every call threw
        // ObjectDisposedException ("handle is destroyed"). Only reachable when the group HAS
        // committed offsets, which is the one case anyone runs this for.
        return partitionOffsets.Select(po =>
        {
            var wm = consumer.QueryWatermarkOffsets(
                new TopicPartition(po.Topic, po.Partition), TimeSpan.FromSeconds(5));
            var current = po.Offset.IsSpecial ? 0 : po.Offset.Value;
            return new PartitionLag(po.Topic, po.Partition.Value, current, wm.High.Value,
                wm.High.Value - current);
        }).ToList();
    }
}
