using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Exceptions;
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
    public async Task RecreateTopicWithFewerPartitionsAsync_reports_the_stage_and_preserves_config_when_the_create_fails()
    {
        using var session = Session();
        var svc = new TopicService();

        var adminCfg = new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using (var admin = new AdminClientBuilder(adminCfg).Build())
        {
            await admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = "fail-topic",
                    NumPartitions = 4,
                    ReplicationFactor = 1,
                    Configs = new Dictionary<string, string> { ["retention.ms"] = "604800000" }
                }
            });
        }

        // Replication factor 99 on a single-broker container makes CreateTopics fail AFTER the
        // delete has already happened - the exact failure mode the user must survive.
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, "fail-topic", 2, 99));

        Assert.Equal(TopicRecreateStage.Creating, ex.Stage);
        Assert.True(ex.TopicMayBeDeleted);

        // Without this the only record of how the topic was configured dies with the local
        // variable, and the app's own "New Topic" form cannot restore it - it takes no configs.
        Assert.Equal("604800000", ex.PreservedConfig["retention.ms"]);
    }

    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_does_not_claim_data_loss_when_it_fails_before_deleting()
    {
        using var session = Session();
        var svc = new TopicService();

        // No such topic: the failure happens while reading state, before anything destructive.
        // ThrowsAsync matches the type exactly, so this also asserts it is NOT the typed
        // TopicRecreateFailedException - a pre-delete failure must never carry a data-loss verdict.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, "absent-topic", 1, 1));
    }

    [Fact]
    public async Task GetTopicDetailAsync_does_not_auto_create_an_unknown_topic()
    {
        using var session = Session();
        var svc = new TopicService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GetTopicDetailAsync(session, "detail-never-existed"));

        // Opening the detail view of a topic someone else just deleted must report that, not
        // silently recreate it empty with the broker's default partition count.
        var topics = await svc.ListTopicsAsync(session);
        Assert.DoesNotContain(topics, t => t.Name == "detail-never-existed");
    }

    [Fact]
    public async Task GetTopicConfigOverridesAsync_returns_nothing_when_the_topic_overrides_nothing()
    {
        using var session = Session();
        var svc = new TopicService();
        await svc.CreateTopicAsync(session, "plain-topic", 1, 1);

        var config = await svc.GetTopicConfigOverridesAsync(session, "plain-topic");

        // Values inherited from the broker's server.properties report a source of
        // StaticBrokerConfig, not DefaultConfig, so an !IsDefault filter lets them through and
        // the recreate path would write them back as permanent topic-level overrides - freezing
        // the topic against any future cluster-wide change.
        Assert.True(config.Count == 0,
            $"Expected no overrides, got {config.Count}: {string.Join(", ", config.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [Fact]
    public async Task GetTopicConfigOverridesAsync_ignores_config_inherited_from_dynamic_broker_settings()
    {
        using var session = Session();
        var svc = new TopicService();

        var adminCfg = new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using (var admin = new AdminClientBuilder(adminCfg).Build())
        {
            // What an operator does with `kafka-configs --entity-type brokers --alter`. These land
            // as DynamicBrokerConfig with IsDefault=false, so they are the highest-impact leak:
            // pinning them onto a topic freezes it against every later cluster-wide change.
            await admin.IncrementalAlterConfigsAsync(new Dictionary<ConfigResource, List<ConfigEntry>>
            {
                [new ConfigResource { Type = ResourceType.Broker, Name = "1" }] = new()
                {
                    new ConfigEntry
                    {
                        Name = "log.retention.ms", Value = "111111111",
                        IncrementalOperation = AlterConfigOpType.Set
                    },
                    new ConfigEntry
                    {
                        Name = "log.cleanup.policy", Value = "compact",
                        IncrementalOperation = AlterConfigOpType.Set
                    }
                }
            });
        }

        await svc.CreateTopicAsync(session, "inherits-topic", 1, 1);

        var config = await svc.GetTopicConfigOverridesAsync(session, "inherits-topic");

        // Guards the mutation `.Where(e => !e.IsDefault && e.Source != StaticBrokerConfig)`, which
        // passes every other test in this suite while reintroducing the bug on any real cluster.
        Assert.True(config.Count == 0,
            $"Expected no overrides, got {config.Count}: {string.Join(", ", config.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [Fact]
    public async Task GetTopicConfigOverridesAsync_returns_overridden_config_values()
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
        var config = await svc.GetTopicConfigOverridesAsync(session, "config-topic");

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
                    Configs = new Dictionary<string, string>
                    {
                        ["retention.ms"] = "7200000",
                        // Explicitly set, but its value equals the cluster default. A filter that
                        // compared VALUES instead of sources would silently drop this one.
                        ["min.insync.replicas"] = "1"
                    }
                }
            });
        }

        var svc = new TopicService();
        await svc.RecreateTopicWithFewerPartitionsAsync(session, "shrink-topic", 2, 1);

        var detail = await svc.GetTopicDetailAsync(session, "shrink-topic");
        Assert.Equal(2, detail.Partitions.Count);

        var config = await svc.GetTopicConfigOverridesAsync(session, "shrink-topic");
        Assert.Equal("7200000", config["retention.ms"]);
        Assert.Equal("1", config["min.insync.replicas"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    public async Task RecreateTopicWithFewerPartitionsAsync_rejects_invalid_count_without_deleting(int requested)
    {
        using var session = Session();
        var svc = new TopicService();
        var topic = $"guard-topic-{(requested < 0 ? "neg" : requested.ToString())}";
        await svc.CreateTopicAsync(session, topic, 4, 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, topic, requested, 1));

        // The guard must run BEFORE the delete: the topic has to be untouched.
        var detail = await svc.GetTopicDetailAsync(session, topic);
        Assert.Equal(4, detail.Partitions.Count);
    }

    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_rejects_unknown_topic()
    {
        using var session = Session();
        var svc = new TopicService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, "no-such-topic-here", 1, 1));
    }

    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_does_not_auto_create_the_topic_it_rejects()
    {
        using var session = Session();
        var svc = new TopicService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, "never-existed", 1, 1));

        // Asking a broker for metadata about a single named topic auto-creates it when
        // auto.create.topics.enable is on. Rejecting a typo'd name must not create it.
        var topics = await svc.ListTopicsAsync(session);
        Assert.DoesNotContain(topics, t => t.Name == "never-existed");
    }

    [Theory]
    [InlineData(3, 1)]   // lower bound: reduce all the way to a single partition
    [InlineData(4, 3)]   // upper bound: reduce by exactly one
    public async Task RecreateTopicWithFewerPartitionsAsync_accepts_both_ends_of_the_valid_range(
        int initialCount, int requested)
    {
        using var session = Session();
        var svc = new TopicService();
        var topic = $"bound-topic-{initialCount}-{requested}";
        await svc.CreateTopicAsync(session, topic, initialCount, 1);

        await svc.RecreateTopicWithFewerPartitionsAsync(session, topic, requested, 1);

        // Without this, tightening the guard to `newPartitionCount <= 1` would keep every other
        // test green while breaking the most common real request: shrink down to one partition.
        var detail = await svc.GetTopicDetailAsync(session, topic);
        Assert.Equal(requested, detail.Partitions.Count);
    }

    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_explains_that_a_single_partition_cannot_shrink()
    {
        using var session = Session();
        var svc = new TopicService();
        await svc.CreateTopicAsync(session, "solo-topic", 1, 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, "solo-topic", 1, 1));

        Assert.Contains("nothing to reduce", ex.Message);
        Assert.DoesNotContain("between 1 and 0", ex.Message);
    }
}
