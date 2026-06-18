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
}
