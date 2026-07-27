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

    [Fact]
    public async Task GetGroupLagAsync_reports_lag_for_a_group_with_committed_offsets()
    {
        // The path nothing exercised until the TUI called it: the method builds a consumer, returns
        // a deferred Select over it, and disposes it on the way out - so enumeration happened after
        // the native handle was destroyed. It only fails when the group HAS committed offsets, which
        // is the only case anyone runs this for.
        var bootstrap = _kafka.GetBootstrapAddress();
        var topic = $"lag-topic-{Guid.NewGuid():N}";
        const string group = "lag-group";

        var producerCfg = new ProducerConfig { BootstrapServers = bootstrap };
        using (var producer = new ProducerBuilder<Null, string>(producerCfg).Build())
        {
            for (var i = 0; i < 5; i++)
                await producer.ProduceAsync(topic, new Message<Null, string> { Value = $"m{i}" });
            producer.Flush(TimeSpan.FromSeconds(5));
        }

        // Consume two of the five and commit, leaving a known lag of three.
        var consumerCfg = new ConsumerConfig
        {
            BootstrapServers = bootstrap, GroupId = group,
            AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false
        };
        using (var consumer = new ConsumerBuilder<Null, string>(consumerCfg).Build())
        {
            consumer.Subscribe(topic);
            for (var i = 0; i < 2; i++) consumer.Consume(TimeSpan.FromSeconds(10));
            consumer.Commit();
            consumer.Close();
        }

        var svc = new ClusterService();
        using var session = Session();

        var lags = (await svc.GetGroupLagAsync(session, group)).ToList();

        var forTopic = lags.Where(l => l.Topic == topic).ToList();
        Assert.NotEmpty(forTopic);
        Assert.Equal(3, forTopic.Sum(l => l.Lag));
    }
}
