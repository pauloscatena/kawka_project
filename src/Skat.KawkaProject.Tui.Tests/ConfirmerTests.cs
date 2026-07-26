using Spectre.Console;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

/// <summary>
/// The tests that stand between a mistyped script and a deleted topic. Everything else in this
/// project can be wrong and be fixed; this cannot.
/// </summary>
public class ConfirmerTests
{
    private static IAnsiConsole Console(StringWriter sink)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(sink)
        });

        // Wide enough that nothing wraps, so these tests assert on the wording rather than on
        // where a panel happened to break a line.
        console.Profile.Width = 500;
        return console;
    }

    private static IAnsiConsole Silent() => Console(new StringWriter());

    private static DestructiveAction Recreate(string topic = "orders") =>
        DestructiveAction.Recreate(topic);

    [Fact]
    public async Task Interactive_accepts_only_the_exact_topic_name()
    {
        var confirmer = new InteractiveConfirmer(Silent(), () => "orders");

        Assert.True(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }

    [Theory]
    [InlineData("Orders")]      // wrong case
    [InlineData("order")]       // truncated
    [InlineData("orders ")]     // trailing space
    [InlineData(" orders")]     // leading space
    [InlineData("y")]           // muscle memory from other tools
    [InlineData("yes")]
    [InlineData("")]
    [InlineData(null)]          // EOF
    public async Task Interactive_rejects_anything_else(string? typed)
    {
        var confirmer = new InteractiveConfirmer(Silent(), () => typed);

        Assert.False(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }

    [Fact]
    public async Task Interactive_asks_once_and_does_not_re_prompt()
    {
        // Re-prompting turns a typo into "keep going until it works", which is the opposite of what
        // a confirmation is for.
        var reads = 0;
        var confirmer = new InteractiveConfirmer(Silent(), () => { reads++; return "wrong"; });

        await confirmer.ConfirmAsync(Recreate(), CancellationToken.None);

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task Interactive_shows_what_will_be_lost_before_asking()
    {
        // The gate is only meaningful if the operator can read the consequences first.
        var sink = new StringWriter();

        await new InteractiveConfirmer(Console(sink), () => "no").ConfirmAsync(Recreate(), CancellationToken.None);

        var shown = sink.ToString();
        foreach (var loss in DestructiveAction.RecreateLoses)
            Assert.Contains(loss, shown);
    }

    [Fact]
    public async Task Interactive_also_shows_what_survives()
    {
        // Listing only the losses sends the operator off to re-apply configuration that the
        // operation already carried over.
        var sink = new StringWriter();

        await new InteractiveConfirmer(Console(sink), () => "no").ConfirmAsync(Recreate(), CancellationToken.None);

        foreach (var kept in DestructiveAction.RecreatePreserves)
            Assert.Contains(kept, sink.ToString());
    }

    [Fact]
    public async Task Interactive_names_the_topic_it_is_about_to_destroy()
    {
        var sink = new StringWriter();

        await new InteractiveConfirmer(Console(sink), () => "no")
            .ConfirmAsync(Recreate("payments-prod"), CancellationToken.None);

        Assert.Contains("payments-prod", sink.ToString());
    }

    [Fact]
    public async Task A_topic_name_with_markup_characters_does_not_break_the_prompt()
    {
        // Topic names are user-chosen. A name containing brackets must not throw here of all
        // places - the exception would land in the middle of a destructive confirmation.
        var sink = new StringWriter();
        var confirmer = new InteractiveConfirmer(Console(sink), () => "od[d]");

        var ex = await Record.ExceptionAsync(() =>
            confirmer.ConfirmAsync(Recreate("od[d]"), CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task NonInteractive_refuses_by_default()
    {
        var confirmer = new NonInteractiveConfirmer(acknowledged: false, Silent());

        Assert.False(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }

    [Fact]
    public async Task NonInteractive_proceeds_only_with_the_explicit_flag()
    {
        var confirmer = new NonInteractiveConfirmer(acknowledged: true, Silent());

        Assert.True(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }

    [Fact]
    public async Task NonInteractive_says_how_to_proceed_and_what_it_would_cost()
    {
        // A cron job that hits this prints something an operator can act on, rather than failing
        // with no explanation.
        var sink = new StringWriter();

        await new NonInteractiveConfirmer(acknowledged: false, Console(sink))
            .ConfirmAsync(Recreate(), CancellationToken.None);

        var shown = sink.ToString();
        Assert.Contains(NonInteractiveConfirmer.AcknowledgeFlag, shown);
        Assert.Contains("orders", shown);
        Assert.Contains(DestructiveAction.LostMessages, shown);
    }

    [Fact]
    public async Task NonInteractive_says_nothing_when_it_was_told_to_proceed()
    {
        // The refusal notice is for the refusal. Printing it on the way through would put a scary
        // paragraph into the output of a job that is working as intended.
        var sink = new StringWriter();

        await new NonInteractiveConfirmer(acknowledged: true, Console(sink))
            .ConfirmAsync(Recreate(), CancellationToken.None);

        Assert.Equal("", sink.ToString());
    }

    [Fact]
    public void The_acknowledge_flag_is_deliberately_hard_to_type_by_accident()
    {
        // If this ever becomes --force or -y, someone's muscle memory deletes a production topic.
        Assert.Equal("yes-i-know-this-deletes-data", NonInteractiveConfirmer.AcknowledgeFlag);
    }
}
