using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class TopicCommandsTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private static IKafkaSession FakeSession()
    {
        var m = new Mock<IKafkaSession>();
        m.Setup(s => s.ProfileName).Returns("test");
        return m.Object;
    }

    private static CommandContext Ctx(string line) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = FakeSession(),
        Confirmer = new NoConfirmer()
    };

    private static Mock<ITopicService> OrdersWith(IDictionary<string, string> overrides)
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 2, 3),
               new List<PartitionInfo> { new(0, 1, 0, 1204), new(1, 2, 0, 987) }));
        svc.Setup(s => s.GetTopicConfigOverridesAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new Dictionary<string, string>(overrides));
        return svc;
    }

    [Fact]
    public async Task Topics_lists_every_topic()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 3), new TopicInfo("payments", 8, 3) });

        var result = await new TopicsCommand(svc.Object).ExecuteAsync(Ctx("topics"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public async Task Topics_filters_by_substring_case_insensitively()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 3), new TopicInfo("payments", 8, 3) });

        var result = await new TopicsCommand(svc.Object).ExecuteAsync(Ctx("topics ORDER"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Single(table.Rows);
        Assert.Equal("orders", table.Rows[0][0]);
    }

    [Fact]
    public async Task Topics_on_an_empty_cluster_is_an_empty_table()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(Array.Empty<TopicInfo>());

        var result = await new TopicsCommand(svc.Object).ExecuteAsync(Ctx("topics"), CancellationToken.None);

        Assert.Empty(Assert.IsType<CommandResult.Table>(result).Rows);
    }

    [Fact]
    public async Task Describe_returns_partitions_and_config_overrides()
    {
        var svc = OrdersWith(new Dictionary<string, string> { ["retention.ms"] = "604800000" });

        var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

        var group = Assert.IsType<CommandResult.Group>(result);
        var table = Assert.IsType<CommandResult.Table>(group.Parts.Last());
        Assert.Equal(2, table.Rows.Count);

        // Identity and overrides are separate parts: a config named "topic" must not be able to
        // overwrite the topic's own name.
        var overrides = group.Parts.OfType<CommandResult.Pairs>()
            .Single(p => p.Values.ContainsKey("retention.ms"));
        Assert.Equal("604800000", overrides.Values["retention.ms"]);
    }

    [Fact]
    public async Task Describe_survives_the_pipe_with_its_overrides_intact()
    {
        // The point of not hiding them in a title. PlainTextRenderer drops titles by design, so a
        // piped describe used to show partitions and silently omit the overrides.
        var svc = OrdersWith(new Dictionary<string, string> { ["retention.ms"] = "604800000" });
        var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(result);

        Assert.Contains("retention.ms", output.ToString());
        Assert.Contains("604800000", output.ToString());
    }

    [Fact]
    public async Task Describe_says_there_are_no_overrides_rather_than_showing_nothing()
    {
        // An empty result means "no overrides", not "nobody looked". Silence would read as the
        // latter, and the difference matters before a destructive operation.
        var svc = OrdersWith(new Dictionary<string, string>());

        var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(result);

        Assert.Contains("no config overrides", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Describe_reports_the_topic_facts_as_data_not_decoration()
    {
        var svc = OrdersWith(new Dictionary<string, string>());

        var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

        var pairs = Assert.IsType<CommandResult.Pairs>(Assert.IsType<CommandResult.Group>(result).Parts.First());
        Assert.Equal("orders", pairs.Values["topic"]);
        Assert.Equal("2", pairs.Values["partitions"]);
        Assert.Equal("3", pairs.Values["replication factor"]);
    }

    [Fact]
    public async Task Describe_without_a_topic_name_is_a_usage_error()
    {
        var result = await new DescribeCommand(Mock.Of<ITopicService>())
            .ExecuteAsync(Ctx("describe"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Offsets_are_written_the_same_way_whatever_the_server_locale_is()
    {
        // This tool runs over SSH on servers with any locale. Under pt-BR, "N0" turns 1204 into
        // "1.204", and `describe orders | cut -f4` hands a script a number it cannot parse.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            var svc = OrdersWith(new Dictionary<string, string>());

            var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

            var output = new StringWriter();
            new PlainTextRenderer(output, new StringWriter()).Render(result);
            Assert.Contains("1204", output.ToString());
            Assert.DoesNotContain("1.204", output.ToString());
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    [Fact]
    public async Task A_config_override_cannot_overwrite_the_topics_own_identity()
    {
        // No Kafka config is called "topic" today, so this is not reachable from a real cluster -
        // but merging both into one dictionary means the day one is, describe would name the wrong
        // topic on the screen someone reads before deleting it.
        var svc = OrdersWith(new Dictionary<string, string>
        {
            ["topic"] = "HIJACKED",
            ["partitions"] = "999"
        });

        var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

        var identity = Assert.IsType<CommandResult.Pairs>(Assert.IsType<CommandResult.Group>(result).Parts.First());
        Assert.Equal("orders", identity.Values["topic"]);
        Assert.Equal("2", identity.Values["partitions"]);
    }

    [Fact]
    public async Task A_failure_reading_config_does_not_throw_away_the_partitions_already_fetched()
    {
        // Two network calls. Losing the first call's answer because the second timed out means the
        // operator sees nothing at all, having already paid for the data that did arrive.
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 2, 3),
               new List<PartitionInfo> { new(0, 1, 0, 1204), new(1, 2, 0, 987) }));
        svc.Setup(s => s.GetTopicConfigOverridesAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ThrowsAsync(new InvalidOperationException("describe configs timed out"));

        var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

        var output = new StringWriter();
        var errors = new StringWriter();
        new PlainTextRenderer(output, errors).Render(result);

        Assert.Contains("1204", output.ToString());                       // partitions survived
        Assert.Contains("timed out", errors.ToString());                  // and the failure is stated
        Assert.NotEqual(ExitCodes.Success, result.ExitCodeOrSuccess);     // without pretending it worked
    }

    [Fact]
    public async Task Topics_are_ordered_the_way_they_are_filtered()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 1, 1), new TopicInfo("Orders-archive", 1, 1) });

        var result = await new TopicsCommand(svc.Object).ExecuteAsync(Ctx("topics"), CancellationToken.None);

        // Ordinal would sort every capitalised name into its own block ahead of the lowercase ones,
        // which reads as "the list is not sorted" to anyone scanning it.
        var names = Assert.IsType<CommandResult.Table>(result).Rows.Select(r => r[0]).ToArray();
        Assert.Equal(new[] { "orders", "Orders-archive" }, names);
    }

    [Fact]
    public void A_group_carrying_a_failure_reports_that_failures_exit_code()
    {
        var group = new CommandResult.Group(new CommandResult[]
        {
            new CommandResult.Text("fine"),
            new CommandResult.Failure("nope", ExitCodes.Usage)
        });

        Assert.Equal(ExitCodes.Usage, group.ExitCodeOrSuccess);
    }
}
