namespace Skat.KawkaProject.Core.Models;

public record PartitionLag(string Topic, int Partition, long CurrentOffset, long EndOffset, long Lag);
