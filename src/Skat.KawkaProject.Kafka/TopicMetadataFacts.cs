using Confluent.Kafka;

namespace Skat.KawkaProject.Kafka;

/// <summary>Pure derivations over Kafka topic metadata, shared by the adapter and the recreate
/// saga without either depending on the other.</summary>
internal static class TopicMetadataFacts
{
    /// <summary>
    /// Minimum replica count across partitions, not partition 0's. A non-uniform assignment (an
    /// interrupted reassignment) would otherwise report partition 0's factor, and a recreate built
    /// from it would ask for that factor uniformly - misrepresenting the topic's real durability.
    /// The minimum tells the truth about the weakest partition. DefaultIfEmpty(0) avoids
    /// IndexOutOfRange for a topic reporting no partitions; a caller acting on the result must
    /// refuse that 0 rather than pass it on - see <see cref="FactsFor"/>.
    /// </summary>
    public static short ReplicationFactorOf(IEnumerable<int> replicaCountsPerPartition) =>
        (short)replicaCountsPerPartition.DefaultIfEmpty(0).Min();

    /// <summary>
    /// The partition count and replication factor a recreate must rebuild the topic with, or a
    /// refusal explaining what the cluster failed to answer.
    /// </summary>
    /// <remarks>
    /// Every refusal here happens BEFORE the first destructive call, which is the whole point of
    /// validating this early: the factor derived here only reaches a TopicSpecification inside the
    /// create-retry, that is, after the delete already succeeded. A value this method lets through
    /// and the broker later rejects costs the topic - it is deleted, the create is refused as a
    /// permanent error, and nothing replaces it.
    /// </remarks>
    public static (int PartitionCount, short ReplicationFactor) FactsFor(TopicMetadata? topic, string topicName)
    {
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

        // The guard above reads the TOPIC's error, but both numbers below are derived from the
        // PARTITIONS, and a partition carries its own. A broker mid-election answers
        // LeaderNotAvailable per partition while the topic itself looks healthy - and those are
        // exactly the partitions whose replica lists cannot be trusted.
        var degraded = topic.Partitions.Where(p => p.Error.IsError).ToList();
        if (degraded.Count > 0)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' has {degraded.Count} partition(s) reporting an error " +
                $"(partition {degraded[0].PartitionId}: {degraded[0].Error.Reason}). " +
                "Refusing to recreate it until the cluster answers reliably.");
        }

        // .Replicas is int[] (same access ListTopicsAsync/GetTopicDetailAsync already use: .Length).
        var replicationFactor = ReplicationFactorOf(topic.Partitions.Select(p => p.Replicas.Length));

        // Kafka requires RF >= 1, so a derived 0 cannot rebuild anything - and it is reachable with
        // every guard above passing: the factor is the MINIMUM across partitions, so one partition
        // whose replica list came back empty drags it to 0 while the topic-level error stays
        // NoError and the partition count stays non-zero. Refusing here costs the user a retry;
        // letting it through costs them the topic.
        if (replicationFactor < 1)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' reported a replication factor of {replicationFactor} " +
                "(at least one partition came back with no replicas); refusing to recreate it, " +
                "because the cluster would only reject the new topic after the old one was deleted.");
        }

        return (topic.Partitions.Count, replicationFactor);
    }
}
