using Moq;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class TopicAdminCommandsTests
{
    private sealed class FixedConfirmer(bool answer) : IConfirmer
    {
        public DestructiveAction? Seen { get; private set; }
        public int Calls { get; private set; }

        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct)
        {
            Seen = a;
            Calls++;
            return Task.FromResult(answer);
        }
    }

    private static CommandContext Ctx(string line, IConfirmer confirmer) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = Mock.Of<IKafkaSession>(),
        Confirmer = confirmer
    };

    private static TopicRecreateFailedException RecreateFailed(bool topicMayBeDeleted) =>
        new(TopicRecreateStage.Creating, topicMayBeDeleted,
            new TopicRecreateAttempt("orders", 4, 2, 3,
                new Dictionary<string, string> { ["retention.ms"] = "604800000" }),
            "could not recreate", new InvalidOperationException("broker down"));

    [Fact]
    public async Task Delete_does_nothing_when_confirmation_is_refused()
    {
        var svc = new Mock<ITopicService>();
        var confirmer = new FixedConfirmer(false);

        var result = await new DeleteCommand(svc.Object).ExecuteAsync(Ctx("delete orders", confirmer), CancellationToken.None);

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(ExitCodes.ConfirmationRefused, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Delete_proceeds_when_confirmed()
    {
        var svc = new Mock<ITopicService>();

        await new DeleteCommand(svc.Object).ExecuteAsync(Ctx("delete orders", new FixedConfirmer(true)), CancellationToken.None);

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "orders"), Times.Once);
    }

    [Fact]
    public async Task Delete_asks_before_it_acts_not_after()
    {
        // Ordering is the whole gate: confirming a deletion that already happened is theatre.
        var order = new List<string>();
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>()))
           .Callback(() => order.Add("deleted")).Returns(Task.CompletedTask);

        var confirmer = new Mock<IConfirmer>();
        confirmer.Setup(c => c.ConfirmAsync(It.IsAny<DestructiveAction>(), It.IsAny<CancellationToken>()))
                 .Callback(() => order.Add("asked")).ReturnsAsync(true);

        await new DeleteCommand(svc.Object).ExecuteAsync(Ctx("delete orders", confirmer.Object), CancellationToken.None);

        Assert.Equal(new[] { "asked", "deleted" }, order);
    }

    [Fact]
    public async Task Delete_describes_the_delete_not_a_recreate()
    {
        var confirmer = new FixedConfirmer(false);

        await new DeleteCommand(Mock.Of<ITopicService>()).ExecuteAsync(Ctx("delete orders", confirmer), CancellationToken.None);

        Assert.Equal("delete", confirmer.Seen!.Verb);
        Assert.Empty(confirmer.Seen.WhatIsPreserved);   // nothing survives a delete
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("recreate --to 2")]
    public async Task A_destructive_command_without_a_topic_never_reaches_the_confirmer(string line)
    {
        var confirmer = new FixedConfirmer(true);

        var result = line.StartsWith("delete", StringComparison.Ordinal)
            ? await new DeleteCommand(Mock.Of<ITopicService>()).ExecuteAsync(Ctx(line, confirmer), CancellationToken.None)
            : await new RecreateCommand(Mock.Of<ITopicService>()).ExecuteAsync(Ctx(line, confirmer), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
        Assert.Equal(0, confirmer.Calls);
    }

    [Theory]
    [InlineData("delete \"\"")]
    [InlineData("delete \"   \"")]
    public async Task A_blank_topic_name_is_rejected_before_anything_destructive_happens(string line)
    {
        // The parser keeps a quoted empty token, so this gets past a bare Args.Count check. The
        // confirmer refuses a blank name too, but the command should not have got that far.
        var svc = new Mock<ITopicService>();
        var confirmer = new FixedConfirmer(true);

        var result = await new DeleteCommand(svc.Object).ExecuteAsync(Ctx(line, confirmer), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
        Assert.Equal(0, confirmer.Calls);
        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>()), Times.Never);
    }

    private static Mock<ITopicService> TopicWith(int partitions)
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", partitions, 1),
               Enumerable.Range(0, partitions).Select(i => new PartitionInfo(i, 1, 0, 0)).ToList()));
        return svc;
    }

    [Theory]
    [InlineData(8)]     // more than it has
    [InlineData(4)]     // exactly what it has
    public async Task Recreate_to_a_count_that_is_not_a_reduction_never_asks_for_confirmation(int target)
    {
        // The service refuses this anyway, so nothing was at risk - but making someone type a topic
        // name to authorise an operation that cannot run teaches them to type it without reading.
        var svc = TopicWith(4);
        var confirmer = new FixedConfirmer(true);

        var result = await new RecreateCommand(svc.Object)
            .ExecuteAsync(Ctx($"recreate orders --to {target}", confirmer), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
        Assert.Equal(0, confirmer.Calls);
        svc.Verify(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task Recreate_says_which_counts_would_work()
    {
        var result = await new RecreateCommand(TopicWith(4).Object)
            .ExecuteAsync(Ctx("recreate orders --to 8", new FixedConfirmer(true)), CancellationToken.None);

        var message = Assert.IsType<CommandResult.Failure>(result).Message;
        Assert.Contains("1 and 3", message);
        Assert.Contains("increase", message);   // points at the command that does grow a topic
    }

    [Fact]
    public async Task Recreate_tells_the_confirmer_everything_that_will_be_lost()
    {
        var svc = TopicWith(4);
        var confirmer = new FixedConfirmer(false);

        await new RecreateCommand(svc.Object).ExecuteAsync(Ctx("recreate orders --to 2", confirmer), CancellationToken.None);

        Assert.NotNull(confirmer.Seen);
        Assert.Contains(confirmer.Seen!.WhatIsLost, w => w.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(confirmer.Seen.WhatIsLost, w => w.Contains("offset", StringComparison.OrdinalIgnoreCase));
        // Deliberately not asserting ACLs: literal ACLs on the same name survive delete+recreate,
        // and the canonical list excludes them on purpose.
    }

    [Fact]
    public async Task Recreate_does_nothing_when_confirmation_is_refused()
    {
        var svc = TopicWith(4);

        var result = await new RecreateCommand(svc.Object)
            .ExecuteAsync(Ctx("recreate orders --to 2", new FixedConfirmer(false)), CancellationToken.None);

        svc.Verify(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
        Assert.Equal(ExitCodes.ConfirmationRefused, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Recreate_surfaces_the_preserved_config_when_the_topic_may_be_gone()
    {
        var svc = TopicWith(4);
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .ThrowsAsync(RecreateFailed(topicMayBeDeleted: true));

        var result = await new RecreateCommand(svc.Object)
            .ExecuteAsync(Ctx("recreate orders --to 2", new FixedConfirmer(true)), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        // The terminal scrollback is the user's only record of the destroyed topic's config.
        Assert.Contains("retention.ms=604800000", failure.Message);
        Assert.Contains("DATA LOSS", failure.Message);
    }

    [Fact]
    public async Task A_refused_delete_does_not_cry_data_loss()
    {
        // The broker refusing the delete (ACL, delete.topic.enable=false) leaves the topic intact.
        // A maximum-severity warning here teaches the operator to dismiss the one that matters.
        var svc = TopicWith(4);
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .ThrowsAsync(RecreateFailed(topicMayBeDeleted: false));

        var result = await new RecreateCommand(svc.Object)
            .ExecuteAsync(Ctx("recreate orders --to 2", new FixedConfirmer(true)), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.DoesNotContain("DATA LOSS", failure.Message);
        Assert.Contains("NOT modified", failure.Message);
    }

    [Fact]
    public async Task Increase_requires_the_to_flag()
    {
        var result = await new IncreaseCommand(Mock.Of<ITopicService>())
            .ExecuteAsync(Ctx("increase orders", new FixedConfirmer(true)), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Increase_never_asks_for_confirmation()
    {
        // Growing a partition count destroys nothing. A confirmation prompt here would train the
        // operator to type topic names without reading, for the times it does matter.
        var confirmer = new FixedConfirmer(true);

        await new IncreaseCommand(Mock.Of<ITopicService>())
            .ExecuteAsync(Ctx("increase orders --to 8", confirmer), CancellationToken.None);

        Assert.Equal(0, confirmer.Calls);
    }

    [Fact]
    public async Task Create_passes_partitions_and_replication()
    {
        var svc = new Mock<ITopicService>();

        await new CreateCommand(svc.Object).ExecuteAsync(
            Ctx("create orders --partitions 4 --replication 3", new FixedConfirmer(true)), CancellationToken.None);

        svc.Verify(s => s.CreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 4, (short)3), Times.Once);
    }

    [Fact]
    public async Task Create_defaults_the_replication_factor_to_one()
    {
        var svc = new Mock<ITopicService>();

        await new CreateCommand(svc.Object).ExecuteAsync(
            Ctx("create orders --partitions 4", new FixedConfirmer(true)), CancellationToken.None);

        svc.Verify(s => s.CreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 4, (short)1), Times.Once);
    }

    [Fact]
    public async Task Create_requires_a_partition_count()
    {
        var result = await new CreateCommand(Mock.Of<ITopicService>())
            .ExecuteAsync(Ctx("create orders", new FixedConfirmer(true)), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }
}
