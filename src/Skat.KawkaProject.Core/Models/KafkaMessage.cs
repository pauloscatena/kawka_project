namespace Skat.KawkaProject.Core.Models;

public record KafkaMessage(string Topic, int Partition, long Offset, string? Key, string? Value, DateTime Timestamp);
