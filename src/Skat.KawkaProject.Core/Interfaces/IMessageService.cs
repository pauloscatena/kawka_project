using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IMessageService
{
    Task<IEnumerable<KafkaMessage>> FetchMessagesAsync(
        IKafkaSession session, string topicName, int partition, long startOffset, int count);
    IObservable<KafkaMessage> Tail(IKafkaSession session, string topicName);
    Task ProduceAsync(IKafkaSession session, string topicName, string? key, string value);
}
