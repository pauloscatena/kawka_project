using Confluent.Kafka;

namespace Skat.KawkaProject.Kafka;

/// <summary>Pure derivations over Kafka topic metadata, shared by the adapter and the recreate
/// saga without either depending on the other.</summary>
internal static class TopicMetadataFacts
{
    /// <summary>
    /// Minimum replica count across partitions, not partition 0's. A non-uniform assignment (an
    /// interrupted reassignment) would otherwise report partition 0's factor. DefaultIfEmpty(0)
    /// avoids IndexOutOfRange for a topic reporting no partitions.
    /// </summary>
    public static short ReplicationFactorOf(IEnumerable<int> replicaCountsPerPartition) =>
        (short)replicaCountsPerPartition.DefaultIfEmpty(0).Min();
}
