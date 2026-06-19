using System.Reactive.Linq;
using Confluent.Kafka;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class MessageService : IMessageService
{
    public async Task<IEnumerable<KafkaMessage>> FetchMessagesAsync(
        IKafkaSession session, string topicName, int partition, long startOffset, int count)
    {
        var cfg = new ConsumerConfig
        {
            GroupId = $"kawka-fetch-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        ((KafkaSession)session).ApplyTo(cfg);

        return await Task.Run(() =>
        {
            var messages = new List<KafkaMessage>();
            using var consumer = new ConsumerBuilder<string, string>(cfg).Build();
            consumer.Assign(new TopicPartitionOffset(topicName, partition, startOffset));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                while (messages.Count < count)
                {
                    var result = consumer.Consume(cts.Token);
                    if (result?.Message == null) break;
                    messages.Add(ToMessage(result));
                }
            }
            catch (OperationCanceledException) { }

            return (IEnumerable<KafkaMessage>)messages;
        });
    }

    public IObservable<KafkaMessage> Tail(IKafkaSession session, string topicName) =>
        Observable.Create<KafkaMessage>(observer =>
        {
            var cfg = new ConsumerConfig
            {
                GroupId = $"kawka-tail-{Guid.NewGuid()}",
                AutoOffsetReset = AutoOffsetReset.Latest,
                EnableAutoCommit = false
            };
            ((KafkaSession)session).ApplyTo(cfg);

            var consumer = new ConsumerBuilder<string, string>(cfg).Build();
            consumer.Subscribe(topicName);
            var cts = new CancellationTokenSource();

            Task.Run(() =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var result = consumer.Consume(cts.Token);
                        if (result?.Message != null)
                            observer.OnNext(ToMessage(result));
                    }
                    observer.OnCompleted();
                }
                catch (OperationCanceledException) { observer.OnCompleted(); }
                catch (Exception ex) { observer.OnError(ex); }
                finally { consumer.Dispose(); }
            }, cts.Token);

            return () => cts.Cancel();
        });

    public async Task ProduceAsync(IKafkaSession session, string topicName, string? key, string value)
    {
        var cfg = new ProducerConfig();
        ((KafkaSession)session).ApplyTo(cfg);
        using var producer = new ProducerBuilder<string?, string>(cfg).Build();
        await producer.ProduceAsync(topicName, new Message<string?, string> { Key = key, Value = value });
    }

    private static KafkaMessage ToMessage(ConsumeResult<string, string> r) =>
        new(r.Topic, r.Partition.Value, r.Offset.Value,
            r.Message.Key, r.Message.Value,
            r.Message.Timestamp.UtcDateTime);
}
