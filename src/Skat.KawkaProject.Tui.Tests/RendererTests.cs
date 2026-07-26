using Spectre.Console;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Rendering;

namespace Skat.KawkaProject.Tui.Tests;

public class RendererTests
{
    private static CommandResult.Table SampleTable() => new(
        "Topics",
        new[] { "NAME", "PARTS" },
        new IReadOnlyList<string>[] { new[] { "orders", "4" }, new[] { "payments", "8" } });

    [Fact]
    public void PlainTextRenderer_emits_tab_separated_rows_without_ansi()
    {
        var output = new StringWriter();
        var renderer = new PlainTextRenderer(output, new StringWriter());

        renderer.Render(SampleTable());

        var text = output.ToString();
        Assert.Contains("NAME\tPARTS", text);
        Assert.Contains("orders\t4", text);
        Assert.DoesNotContain("[", text);   // no ANSI escapes
        Assert.DoesNotContain("│", text);         // no box drawing
    }

    [Fact]
    public void PlainTextRenderer_writes_failures_to_stderr()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var renderer = new PlainTextRenderer(output, error);

        renderer.Render(new CommandResult.Failure("nope", ExitCodes.Usage));

        Assert.Contains("nope", error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void PlainTextRenderer_omits_the_title_so_stdout_stays_parseable()
    {
        // The title is decoration. A pipeline doing `kawka topics | cut -f1` must not receive a
        // "Topics" line it has to know to skip.
        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(SampleTable());

        Assert.StartsWith("NAME\tPARTS", output.ToString());
    }

    [Fact]
    public void PlainTextRenderer_writes_pairs_as_key_tab_value()
    {
        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(
            new CommandResult.Pairs("Status", new Dictionary<string, string> { ["profile"] = "prod" }));

        Assert.Contains("profile\tprod", output.ToString());
    }

    [Fact]
    public void SpectreRenderer_renders_all_rows()
    {
        var record = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(record)
        });
        var renderer = new SpectreRenderer(console);

        renderer.Render(SampleTable());

        var text = record.ToString();
        Assert.Contains("orders", text);
        Assert.Contains("payments", text);
    }

    [Fact]
    public void SpectreRenderer_does_not_let_data_be_read_as_markup()
    {
        // A topic named "[red]x[/]" is legal in Kafka and must render literally, not as colour -
        // and an unbalanced tag would otherwise throw right in the middle of listing topics.
        var record = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(record)
        });

        new SpectreRenderer(console).Render(new CommandResult.Table(
            null,
            new[] { "NAME" },
            new IReadOnlyList<string>[] { new[] { "[red]not-colour[/]" } }));

        Assert.Contains("[red]not-colour[/]", record.ToString());
    }

    [Fact]
    public void SpectreRenderer_survives_an_unclosed_markup_tag_in_data()
    {
        var record = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(record)
        });

        var ex = Record.Exception(() => new SpectreRenderer(console).Render(
            new CommandResult.Text("half a tag: [bold")));

        Assert.Null(ex);
        Assert.Contains("[bold", record.ToString());
    }
}
