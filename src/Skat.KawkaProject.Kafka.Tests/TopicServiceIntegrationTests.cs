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
}
