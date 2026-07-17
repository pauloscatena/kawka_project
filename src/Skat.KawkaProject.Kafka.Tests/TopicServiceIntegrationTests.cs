using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

public class TopicServiceIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder().Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    private KafkaSession Session() => new(new ConnectionProfile
    {
        Name = "test",
        BootstrapServers = _kafka.GetBootstrapAddress()
    });

    [Fact]
    public async Task ListTopicsAsync_returns_created_topic()
    {
        var svc = new TopicService();
        using var session = Session();
        await svc.CreateTopicAsync(session, "test-topic", 1, 1);
        var topics = await svc.ListTopicsAsync(session);
        Assert.Contains(topics, t => t.Name == "test-topic");
    }

    [Fact]
    public async Task DeleteTopicAsync_removes_topic()
    {
        var svc = new TopicService();
        using var session = Session();
        await svc.CreateTopicAsync(session, "delete-me", 1, 1);
        await svc.DeleteTopicAsync(session, "delete-me");
        var topics = await svc.ListTopicsAsync(session);
        Assert.DoesNotContain(topics, t => t.Name == "delete-me");
    }

    [Fact]
    public async Task GetTopicDetailAsync_returns_partition_offsets()
    {
        var svc = new TopicService();
        using var session = Session();
        await svc.CreateTopicAsync(session, "detail-topic", 2, 1);
        var detail = await svc.GetTopicDetailAsync(session, "detail-topic");
        Assert.Equal(2, detail.Partitions.Count);
    }

    [Fact]
    public async Task GetTopicConfigAsync_returns_overridden_config_values()
    {
        using var session = Session();
        var adminCfg = new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using (var admin = new AdminClientBuilder(adminCfg).Build())
        {
            await admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = "config-topic",
                    NumPartitions = 1,
                    ReplicationFactor = 1,
                    Configs = new Dictionary<string, string> { ["retention.ms"] = "3600000" }
                }
            });
        }

        var svc = new TopicService();
        var config = await svc.GetTopicConfigAsync(session, "config-topic");

        Assert.Equal("3600000", config["retention.ms"]);
    }

    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_reduces_partitions_and_preserves_config()
    {
        using var session = Session();
        var adminCfg = new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using (var admin = new AdminClientBuilder(adminCfg).Build())
        {
            await admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = "shrink-topic",
                    NumPartitions = 4,
                    ReplicationFactor = 1,
                    Configs = new Dictionary<string, string> { ["retention.ms"] = "7200000" }
                }
            });
        }

        var svc = new TopicService();
        await svc.RecreateTopicWithFewerPartitionsAsync(session, "shrink-topic", 2, 1);

        var detail = await svc.GetTopicDetailAsync(session, "shrink-topic");
        Assert.Equal(2, detail.Partitions.Count);

        var config = await svc.GetTopicConfigAsync(session, "shrink-topic");
        Assert.Equal("7200000", config["retention.ms"]);
    }
}
