namespace Skat.KawkaProject.Core.Models;

public record TopicDetail(TopicInfo Topic, IReadOnlyList<PartitionInfo> Partitions);
