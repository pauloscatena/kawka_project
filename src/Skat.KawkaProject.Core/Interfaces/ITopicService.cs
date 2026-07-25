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

    /// <summary>
    /// DELETES the topic and recreates it with fewer partitions. Kafka cannot shrink partitions in
    /// place, so this is destructive: <b>ALL MESSAGES IN THE TOPIC ARE PERMANENTLY LOST</b>.
    /// <para>
    /// Carried over: topic-level config overrides (see <see cref="GetTopicConfigOverridesAsync"/>).
    /// NOT carried over: messages, committed consumer group offsets (consumers may then silently
    /// skip or replay records), and ACLs.
    /// </para>
    /// <para>
    /// The replication factor is derived from the live topic; a non-uniform assignment is flattened
    /// to its minimum.
    /// </para>
    /// <para>
    /// Callers MUST obtain explicit user confirmation before calling this. Throws
    /// <see cref="System.ArgumentOutOfRangeException"/> if <paramref name="newPartitionCount"/> is
    /// not in [1, current-1], <see cref="System.InvalidOperationException"/> if the topic has a
    /// single partition or the cluster does not confirm it reliably (unknown, transient metadata
    /// error, or no partitions reported), and
    /// <see cref="Skat.KawkaProject.Core.Exceptions.TopicRecreateFailedException"/> (carrying the
    /// failed stage, whether the topic may be gone, and everything needed to rebuild it) on any
    /// failure during the sequence.
    /// </para>
    /// </summary>
    Task DeleteAndRecreateTopicAsync(IKafkaSession session, string topicName, int newPartitionCount);
}
