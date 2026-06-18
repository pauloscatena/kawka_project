namespace Skat.KawkaProject.Core.Models;

public record PartitionInfo(int PartitionId, int LeaderBrokerId, long EarliestOffset, long LatestOffset);
