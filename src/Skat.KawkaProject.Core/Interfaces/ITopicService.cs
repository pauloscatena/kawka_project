using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface ITopicService
{
    Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session);
    Task<TopicDetail> GetTopicDetailAsync(IKafkaSession session, string topicName);
    Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor);
    Task DeleteTopicAsync(IKafkaSession session, string topicName);
    Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount);
    Task<IReadOnlyDictionary<string, string>> GetTopicConfigAsync(IKafkaSession session, string topicName);
    Task RecreateTopicWithFewerPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor);
}
