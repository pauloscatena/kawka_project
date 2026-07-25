namespace Skat.KawkaProject.Kafka;

/// <summary>
/// Blocking-call timeouts for the Confluent.Kafka AdminClient, shared where the same value is used
/// by more than one class in this assembly. Only MetadataQueryTimeout is here because it is the only
/// one genuinely shared (TopicService and TopicRecreateOperation both feed it to GetMetadata).
/// Single-consumer timeouts (watermark, deletion grace/poll/timeout) stay next to their use, where
/// their comments explain the choice.
/// </summary>
internal static class KafkaTimeouts
{
    public static readonly TimeSpan MetadataQueryTimeout = TimeSpan.FromSeconds(10);
}
