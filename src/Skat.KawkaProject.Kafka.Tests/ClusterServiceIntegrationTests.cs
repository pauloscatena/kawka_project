using Confluent.Kafka;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

public class ClusterServiceIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder().Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    private KafkaSession Session() => new(new ConnectionProfile
    {
        Name = "test", BootstrapServers = _kafka.GetBootstrapAddress()
    });

    [Fact]
    public async Task ListBrokersAsync_returns_at_least_one_broker()
    {
        var svc = new ClusterService();
        using var session = Session();
        var brokers = await svc.ListBrokersAsync(session);
        Assert.NotEmpty(brokers);
    }

    [Fact]
    public async Task ListConsumerGroupsAsync_returns_created_group()
    {
        var bootstrap = _kafka.GetBootstrapAddress();
        var topic = $"grp-topic-{Guid.NewGuid():N}";
        var producerCfg = new ProducerConfig { BootstrapServers = bootstrap };
        using var producer = new ProducerBuilder<Null, string>(producerCfg).Build();
        await producer.ProduceAsync(topic, new Message<Null, string> { Value = "x" });
        producer.Flush(TimeSpan.FromSeconds(3));

        var consumerCfg = new ConsumerConfig
        {
            BootstrapServers = bootstrap, GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = true
        };
        using var consumer = new ConsumerBuilder<Null, string>(consumerCfg).Build();
        consumer.Subscribe(topic);
        consumer.Consume(TimeSpan.FromSeconds(5));
        consumer.Close();

        var svc = new ClusterService();
        using var session = Session();
        var groups = await svc.ListConsumerGroupsAsync(session);
        Assert.Contains(groups, g => g.GroupId == "test-group");
    }
}
