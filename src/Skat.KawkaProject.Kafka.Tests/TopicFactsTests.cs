using Confluent.Kafka;
using Skat.KawkaProject.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

/// <summary>
/// Unit tests for the guards that run immediately before a recreate starts deleting. These cannot
/// be integration tests: a healthy single-broker container never reports a partition-level error
/// and never omits a replica list, so the states these guards refuse are unreachable there and a
/// weakened guard leaves the whole integration suite green. They are reachable on a real cluster,
/// and every one of them ends with a number being rejected AFTER the topic is already gone.
/// </summary>
public class TopicFactsTests
{
    private static PartitionMetadata Partition(int id, int replicaCount, ErrorCode error = ErrorCode.NoError)
    {
        var replicas = Enumerable.Range(1, replicaCount).ToArray();
        return new PartitionMetadata(id, 1, replicas, replicas, new Error(error));
    }

    private static TopicMetadata Topic(params PartitionMetadata[] partitions) =>
        new("orders", partitions.ToList(), new Error(ErrorCode.NoError));

    [Fact]
    public void A_healthy_topic_reports_its_partition_count_and_replication_factor()
    {
        var (count, rf) = TopicMetadataFacts.FactsFor(Topic(Partition(0, 3), Partition(1, 3)), "orders");

        Assert.Equal(2, count);
        Assert.Equal((short)3, rf);
    }

    [Fact]
    public void A_partition_with_no_replicas_is_refused_before_anything_is_deleted()
    {
        // The factor is the MINIMUM across partitions, so one partition whose replica list came
        // back empty drags it to 0 while the topic-level error stays NoError and the partition
        // count stays non-zero - every other guard passes. A TopicSpecification with RF 0 is only
        // built inside the create retry, i.e. after the delete: the broker rejects it with
        // InvalidReplicationFactor, which is a permanent error, so there is no retry either. The
        // topic is gone, nothing replaces it, and the number that caused it was in hand before the
        // first destructive call.
        var ex = Assert.Throws<InvalidOperationException>(
            () => TopicMetadataFacts.FactsFor(Topic(Partition(0, 2), Partition(1, 0)), "orders"));

        Assert.Contains("replication factor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refusing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_partition_level_error_is_refused_before_anything_is_deleted()
    {
        // The topic-level guard already refuses a cluster that cannot answer reliably, but both
        // numbers returned are derived from the PARTITIONS, whose own errors were not being read.
        // A broker mid-election reports LeaderNotAvailable per partition while the topic itself
        // looks fine, and those partitions' replica lists are exactly the untrustworthy ones.
        var ex = Assert.Throws<InvalidOperationException>(
            () => TopicMetadataFacts.FactsFor(
                Topic(Partition(0, 3), Partition(1, 3, ErrorCode.LeaderNotAvailable)), "orders"));

        Assert.Contains("refusing", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The operator has to be told which partition to go and look at.
        Assert.Contains("partition 1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_topic_the_cluster_does_not_list_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TopicMetadataFacts.FactsFor(null, "orders"));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_topic_level_error_is_refused()
    {
        var topic = new TopicMetadata("orders", new List<PartitionMetadata> { Partition(0, 3) },
            new Error(ErrorCode.LeaderNotAvailable));

        var ex = Assert.Throws<InvalidOperationException>(
            () => TopicMetadataFacts.FactsFor(topic, "orders"));

        Assert.Contains("reliably", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_topic_reporting_no_partitions_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TopicMetadataFacts.FactsFor(Topic(), "orders"));

        Assert.Contains("no partitions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_reads_that_agree_are_trusted()
    {
        Assert.Equal((6, (short)3), TopicMetadataFacts.Agreed((6, 3), (6, 3), "orders"));
    }

    [Fact]
    public void A_partition_count_that_changes_between_reads_is_refused()
    {
        // A broker whose metadata cache is still warming after a restart answers with fewer
        // partitions than the topic has, and nothing in the protocol marks that answer as partial.
        // Acting on it recreates the topic fine - the user asked for a specific count - but records
        // the undercount as OriginalPartitionCount, and that number is what the DATA LOSS RISK
        // message tells the operator to rebuild with if the create then fails.
        var ex = Assert.Throws<InvalidOperationException>(
            () => TopicMetadataFacts.Agreed((3, 3), (6, 3), "orders"));

        Assert.Contains("two different answers", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refusing", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Both readings belong in the message: the operator needs to see what changed.
        Assert.Contains("3 partitions", ex.Message);
        Assert.Contains("6 partitions", ex.Message);
    }

    [Fact]
    public void A_replication_factor_that_changes_between_reads_is_refused()
    {
        // A reassignment completing mid-read. Rebuilding with either factor is a guess about
        // durability, on a path that has already deleted the data by the time it matters.
        var ex = Assert.Throws<InvalidOperationException>(
            () => TopicMetadataFacts.Agreed((6, 2), (6, 3), "orders"));

        Assert.Contains("refusing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
