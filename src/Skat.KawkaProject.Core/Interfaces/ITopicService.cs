using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface ITopicService
{
    Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session);
    Task<TopicDetail> GetTopicDetailAsync(IKafkaSession session, string topicName);
    Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor);
    Task DeleteTopicAsync(IKafkaSession session, string topicName);
    Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount);
    /// <summary>
    /// Returns ONLY the config entries explicitly overridden at topic level. Everything the topic
    /// merely inherits is excluded: Kafka's built-in defaults, static broker config
    /// (<c>server.properties</c>), and dynamic broker config (<c>kafka-configs --entity-type
    /// brokers</c>). An empty result therefore means "this topic overrides nothing", NOT "this
    /// topic has no configuration".
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetTopicConfigOverridesAsync(IKafkaSession session, string topicName);
    Task RecreateTopicWithFewerPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor);
}
