using System.Globalization;
using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class ClusterCommandsTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private static CommandContext Ctx(string line) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = Mock.Of<IKafkaSession>(),
        Confirmer = new NoConfirmer()
    };

    private static Mock<IClusterService> BillingLag()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.GetGroupLagAsync(It.IsAny<IKafkaSession>(), "billing"))
           .ReturnsAsync(new[]
           {
               new PartitionLag("orders", 0, 100, 150, 50),
               new PartitionLag("orders", 1, 200, 210, 10)
           });
        return svc;
    }

    private static string Piped(CommandResult result)
    {
        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(result);
        return output.ToString();
    }

    [Fact]
    public async Task Brokers_marks_the_controller()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListBrokersAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new BrokerInfo(1, "k1", 9092, true), new BrokerInfo(2, "k2", 9092, false) });

        var result = await new BrokersCommand(svc.Object).ExecuteAsync(Ctx("brokers"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal("yes", table.Rows[0][3]);
        Assert.Equal("", table.Rows[1][3]);
    }

    [Fact]
    public async Task Groups_lists_state_and_member_count()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new ConsumerGroupInfo("billing", "Stable", 3) });

        var result = await new GroupsCommand(svc.Object).ExecuteAsync(Ctx("groups"), CancellationToken.None);

        var row = Assert.IsType<CommandResult.Table>(result).Rows.Single();
        Assert.Equal(new[] { "billing", "Stable", "3" }, row);
    }

    [Fact]
    public async Task Lag_totals_the_lag_across_partitions()
    {
        var result = await new LagCommand(BillingLag().Object).ExecuteAsync(Ctx("lag billing"), CancellationToken.None);

        var group = Assert.IsType<CommandResult.Group>(result);
        Assert.Equal(2, Assert.IsType<CommandResult.Table>(group.Parts.Last()).Rows.Count);

        var summary = Assert.IsType<CommandResult.Pairs>(group.Parts.First());
        Assert.Equal("60", summary.Values["total lag"]);
    }

    [Fact]
    public async Task The_total_survives_the_pipe()
    {
        // The total is the number someone runs `lag` for. In a title the plain-text renderer drops
        // it, so `kawka lag billing | ...` would answer everything except the question asked.
        var result = await new LagCommand(BillingLag().Object).ExecuteAsync(Ctx("lag billing"), CancellationToken.None);

        Assert.Contains("total lag", Piped(result));
        Assert.Contains("60", Piped(result));
    }

    [Fact]
    public async Task Lag_numbers_do_not_change_shape_with_the_server_locale()
    {
        // Same trap as describe: "N0" under pt-BR turns 150000 into "150.000", and a script reading
        // the LAG column back as a number gets 150.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
            var svc = new Mock<IClusterService>();
            svc.Setup(s => s.GetGroupLagAsync(It.IsAny<IKafkaSession>(), "billing"))
               .ReturnsAsync(new[] { new PartitionLag("orders", 0, 100, 150_000, 149_900) });

            var text = Piped(await new LagCommand(svc.Object).ExecuteAsync(Ctx("lag billing"), CancellationToken.None));

            Assert.Contains("149900", text);
            Assert.DoesNotContain("149.900", text);
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    [Fact]
    public async Task A_group_with_no_lag_still_reports_a_total_of_zero()
    {
        // "No rows" and "nothing behind" look identical otherwise, and they mean different things:
        // a group with no committed offsets versus a group fully caught up.
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.GetGroupLagAsync(It.IsAny<IKafkaSession>(), "idle"))
           .ReturnsAsync(Array.Empty<PartitionLag>());
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new ConsumerGroupInfo("idle", "Empty", 0) });

        var result = await new LagCommand(svc.Object).ExecuteAsync(Ctx("lag idle"), CancellationToken.None);

        var summary = Assert.IsType<CommandResult.Pairs>(Assert.IsType<CommandResult.Group>(result).Parts.First());
        Assert.Equal("0", summary.Values["total lag"]);
    }

    [Fact]
    public async Task Lag_without_a_group_is_a_usage_error()
    {
        var result = await new LagCommand(Mock.Of<IClusterService>())
            .ExecuteAsync(Ctx("lag"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Brokers_and_groups_are_ordered_predictably()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListBrokersAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new BrokerInfo(3, "k3", 9092, false), new BrokerInfo(1, "k1", 9092, true) });

        var table = Assert.IsType<CommandResult.Table>(
            await new BrokersCommand(svc.Object).ExecuteAsync(Ctx("brokers"), CancellationToken.None));

        Assert.Equal(new[] { "1", "3" }, table.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task Lag_for_a_group_that_does_not_exist_says_so()
    {
        // Otherwise it is indistinguishable from a group that exists and is fully caught up - and
        // "nothing is behind" is a very different answer from "that consumer is not running".
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.GetGroupLagAsync(It.IsAny<IKafkaSession>(), "typo"))
           .ReturnsAsync(Array.Empty<PartitionLag>());
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new ConsumerGroupInfo("billing", "Stable", 1) });

        var result = await new LagCommand(svc.Object).ExecuteAsync(Ctx("lag typo"), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
        Assert.Contains("typo", failure.Message);
    }

    [Fact]
    public async Task A_group_with_lag_costs_only_one_round_trip()
    {
        // The existence check is for the ambiguous empty answer only. Paying for it on every call
        // would double the cost of the command's normal path.
        var svc = BillingLag();

        await new LagCommand(svc.Object).ExecuteAsync(Ctx("lag billing"), CancellationToken.None);

        svc.Verify(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()), Times.Never);
    }

    [Fact]
    public async Task Groups_are_listed_in_a_predictable_order()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[]
           {
               new ConsumerGroupInfo("zeta", "Stable", 1),
               new ConsumerGroupInfo("Alpha", "Empty", 0),
               new ConsumerGroupInfo("beta", "Stable", 2)
           });

        var table = Assert.IsType<CommandResult.Table>(
            await new GroupsCommand(svc.Object).ExecuteAsync(Ctx("groups"), CancellationToken.None));

        Assert.Equal(new[] { "Alpha", "beta", "zeta" }, table.Rows.Select(r => r[0]));
    }
}
