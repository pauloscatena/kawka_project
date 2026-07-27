using System.Globalization;
using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class MessageCommandsTests
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

    private static Mock<ITopicService> TopicWithOffsets(long earliest, long latest)
    {
        var topics = new Mock<ITopicService>();
        topics.Setup(t => t.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
              .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 1, 1),
                  new List<PartitionInfo> { new(0, 1, earliest, latest) }));
        return topics;
    }

    private static string Piped(CommandResult result)
    {
        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(result);
        return output.ToString();
    }

    [Fact]
    public async Task Consume_from_earliest_starts_at_the_earliest_offset()
    {
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 40L, 10))
            .ReturnsAsync(new[] { new KafkaMessage("orders", 0, 40, "k", "v", DateTime.UnixEpoch) });

        var cmd = new ConsumeCommand(msgs.Object, TopicWithOffsets(40, 100).Object);
        var result = await cmd.ExecuteAsync(Ctx("consume orders --from earliest --limit 10"), CancellationToken.None);

        Assert.IsType<CommandResult.Table>(result);
        msgs.Verify(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 40L, 10), Times.Once);
    }

    [Fact]
    public async Task Consume_from_latest_backs_up_by_the_limit()
    {
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 90L, 10))
            .ReturnsAsync(Array.Empty<KafkaMessage>());

        var cmd = new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 100).Object);
        await cmd.ExecuteAsync(Ctx("consume orders --from latest --limit 10"), CancellationToken.None);

        msgs.Verify(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 90L, 10), Times.Once);
    }

    [Fact]
    public async Task Consume_from_latest_does_not_read_before_the_start_of_a_short_topic()
    {
        // A topic with fewer messages than the limit must not ask for a negative offset.
        var msgs = new Mock<IMessageService>();
        var cmd = new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 3).Object);

        await cmd.ExecuteAsync(Ctx("consume orders --from latest --limit 10"), CancellationToken.None);

        msgs.Verify(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 0L, 10), Times.Once);
    }

    [Fact]
    public async Task Consume_accepts_an_explicit_numeric_offset()
    {
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 2, 55L, 5))
            .ReturnsAsync(Array.Empty<KafkaMessage>());

        var cmd = new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 100).Object);
        await cmd.ExecuteAsync(Ctx("consume orders --partition 2 --from 55 --limit 5"), CancellationToken.None);

        msgs.Verify(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 2, 55L, 5), Times.Once);
    }

    [Fact]
    public async Task Consume_from_a_partition_the_topic_does_not_have_says_which_ones_exist()
    {
        var cmd = new ConsumeCommand(Mock.Of<IMessageService>(), TopicWithOffsets(0, 100).Object);

        var ex = await Record.ExceptionAsync(() =>
            cmd.ExecuteAsync(Ctx("consume orders --partition 9 --from earliest"), CancellationToken.None));

        Assert.NotNull(ex);
        Assert.Contains("9", ex!.Message);
    }

    [Fact]
    public async Task Consume_rejects_a_limit_below_one()
    {
        var cmd = new ConsumeCommand(Mock.Of<IMessageService>(), TopicWithOffsets(0, 100).Object);

        var result = await cmd.ExecuteAsync(Ctx("consume orders --limit 0"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Offsets_and_timestamps_do_not_change_shape_with_the_server_locale()
    {
        // An offset is an identifier, not a quantity: "1.234.567" is not an offset anyone can feed
        // back into --from. The timestamp needs a fixed calendar too, or a th-TH server prints
        // Buddhist-era years.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
            var msgs = new Mock<IMessageService>();
            msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, It.IsAny<long>(), It.IsAny<int>()))
                .ReturnsAsync(new[]
                {
                    new KafkaMessage("orders", 0, 1_234_567, "k", "v", new DateTime(2026, 7, 26, 13, 5, 9, DateTimeKind.Utc))
                });

            var text = Piped(await new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 2_000_000).Object)
                .ExecuteAsync(Ctx("consume orders --from 0"), CancellationToken.None));

            Assert.Contains("1234567", text);
            Assert.DoesNotContain("1.234.567", text);
            Assert.Contains("2026-07-26 13:05:09", text);
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    [Fact]
    public async Task A_message_body_with_tabs_and_newlines_stays_one_row()
    {
        // The whole reason the plain-text renderer escapes: a payload is arbitrary bytes, and this
        // is the command that puts one straight into a column.
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync(new[]
            {
                new KafkaMessage("orders", 0, 1, "k", "{\n\t\"a\": 1\n}", DateTime.UnixEpoch),
                new KafkaMessage("orders", 0, 2, "k", "plain", DateTime.UnixEpoch)
            });

        var text = Piped(await new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 10).Object)
            .ExecuteAsync(Ctx("consume orders --from 0"), CancellationToken.None));

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);                              // header + two messages
        Assert.All(lines, l => Assert.Equal(3, l.Count(c => c == '\t')));   // 4 columns, always
    }

    [Fact]
    public async Task A_message_with_no_key_or_body_still_produces_a_row()
    {
        // Tombstones have a null body and are exactly what someone goes looking for.
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync(new[] { new KafkaMessage("orders", 0, 7, null, null, DateTime.UnixEpoch) });

        var result = await new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 10).Object)
            .ExecuteAsync(Ctx("consume orders --from 0"), CancellationToken.None);

        var row = Assert.IsType<CommandResult.Table>(result).Rows.Single();
        Assert.Equal("7", row[0]);
        Assert.Equal("", row[2]);
        Assert.Equal("", row[3]);
    }

    [Fact]
    public async Task Consume_renders_in_a_terminal_without_taking_the_process_down()
    {
        // Every test here rendered through the TSV path, so nothing exercised the renderer a real
        // terminal uses - where consume's own title ("orders[0] from offset 42") was fatal.
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync(new[] { new KafkaMessage("orders", 0, 42, "k", "v", DateTime.UnixEpoch) });

        var result = await new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 100).Object)
            .ExecuteAsync(Ctx("consume orders --from 42"), CancellationToken.None);

        var console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
        {
            Ansi = Spectre.Console.AnsiSupport.No,
            ColorSystem = Spectre.Console.ColorSystemSupport.NoColors,
            Out = new Spectre.Console.AnsiConsoleOutput(new StringWriter())
        });

        Assert.Null(Record.Exception(() => new SpectreRenderer(console).Render(result)));
    }

    [Fact]
    public async Task The_timestamp_column_says_which_clock_it_is_on()
    {
        // The service hands back UTC. A bare "2026-07-26 18:58:46" next to a broker log in local
        // time sends whoever is chasing an incident three hours in the wrong direction.
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync(new[] { new KafkaMessage("orders", 0, 1, null, "v", DateTime.UnixEpoch) });

        var result = await new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 10).Object)
            .ExecuteAsync(Ctx("consume orders --from 0"), CancellationToken.None);

        Assert.Contains(Assert.IsType<CommandResult.Table>(result).Columns,
            c => c.Contains("UTC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Produce_requires_a_value()
    {
        var cmd = new ProduceCommand(Mock.Of<IMessageService>());

        var result = await cmd.ExecuteAsync(Ctx("produce orders"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Produce_sends_key_and_value()
    {
        var msgs = new Mock<IMessageService>();
        var cmd = new ProduceCommand(msgs.Object);

        await cmd.ExecuteAsync(Ctx("produce orders --key k1 --value \"hello world\""), CancellationToken.None);

        msgs.Verify(m => m.ProduceAsync(It.IsAny<IKafkaSession>(), "orders", "k1", "hello world"), Times.Once);
    }

    [Fact]
    public async Task Produce_can_send_an_empty_body()
    {
        // Distinct from omitting --value, and a real thing to want.
        var msgs = new Mock<IMessageService>();

        await new ProduceCommand(msgs.Object)
            .ExecuteAsync(Ctx("produce orders --value \"\""), CancellationToken.None);

        msgs.Verify(m => m.ProduceAsync(It.IsAny<IKafkaSession>(), "orders", null, ""), Times.Once);
    }

    [Fact]
    public async Task Produce_can_send_a_body_that_starts_with_dashes()
    {
        var msgs = new Mock<IMessageService>();

        await new ProduceCommand(msgs.Object)
            .ExecuteAsync(Ctx("produce orders --value \"--not-a-flag\""), CancellationToken.None);

        msgs.Verify(m => m.ProduceAsync(It.IsAny<IKafkaSession>(), "orders", null, "--not-a-flag"), Times.Once);
    }
}
