using Skat.KawkaProject.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

/// <summary>
/// Unit tests for how the replication factor is derived from a topic's partitions. These cannot be
/// integration tests: a single-broker container can only produce uniform assignments (every
/// partition RF 1), where the minimum and partition 0 are identical, so the bug this guards -
/// flattening a non-uniform topic to partition 0's factor - is invisible there.
/// </summary>
public class ReplicationFactorTests
{
    [Fact]
    public void Uniform_assignment_reports_that_factor()
    {
        Assert.Equal((short)3, TopicService.ReplicationFactorOf(new[] { 3, 3, 3, 3 }));
    }

    [Fact]
    public void Non_uniform_assignment_reports_the_MINIMUM_not_partition_zero()
    {
        // An interrupted kafka-reassign-partitions run: partition 0 still has 3 replicas, the rest
        // dropped to 2. Deriving from partition 0 would report 3, and a recreate would then ask for
        // RF 3 uniformly - silently RAISING durability expectations the topic never had. Reporting
        // the minimum tells the truth about the weakest partition.
        Assert.Equal((short)2, TopicService.ReplicationFactorOf(new[] { 3, 2, 2, 2 }));
    }

    [Fact]
    public void Minimum_is_found_regardless_of_position()
    {
        Assert.Equal((short)1, TopicService.ReplicationFactorOf(new[] { 3, 3, 1, 3 }));
    }

    [Fact]
    public void No_partitions_yields_zero_rather_than_throwing()
    {
        // A topic mid-deletion can transiently report no partitions. The old code indexed
        // Partitions[0] here and threw IndexOutOfRange (flagged by the Task 3 review).
        Assert.Equal((short)0, TopicService.ReplicationFactorOf(System.Array.Empty<int>()));
    }
}
