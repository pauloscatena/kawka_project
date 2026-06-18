using Confluent.Kafka;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

public class MessageServiceIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder().Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    private KafkaSession Session() => new(new ConnectionProfile
    {
        Name = "test", BootstrapServers = _kafka.GetBootstrapAddress()
    });

    private async Task ProduceAsync(string topic, string value)
    {
        var cfg = new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using var producer = new ProducerBuilder<Null, string>(cfg).Build();
        await producer.ProduceAsync(topic, new Message<Null, string> { Value = value });
        producer.Flush(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FetchMessagesAsync_returns_produced_messages()
    {
        var topic = $"fetch-test-{Guid.NewGuid():N}";
        await ProduceAsync(topic, "hello");
        await ProduceAsync(topic, "world");

        var svc = new MessageService();
        using var session = Session();
        var messages = (await svc.FetchMessagesAsync(session, topic, 0, 0, 10)).ToList();

        Assert.Equal(2, messages.Count);
        Assert.Equal("hello", messages[0].Value);
    }

    [Fact]
    public async Task Tail_receives_messages_as_observable()
    {
        var topic = $"tail-test-{Guid.NewGuid():N}";
        var svc = new MessageService();
        using var session = Session();

        var received = new List<KafkaMessage>();
        using var sub = svc.Tail(session, topic).Subscribe(m => received.Add(m));

        await Task.Delay(500);
        await ProduceAsync(topic, "live-message");
        await Task.Delay(3000);

        Assert.Single(received);
        Assert.Equal("live-message", received[0].Value);
    }
}
