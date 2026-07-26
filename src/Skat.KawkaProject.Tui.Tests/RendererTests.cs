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
        // Escaped rather than a literal ESC byte: the control character is invisible in most
        // editors, and one stray reformat silently turns this into a check for a bare bracket.
        Assert.DoesNotContain("\u001b[", text);
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

    [Fact]
    public void A_tab_inside_a_value_does_not_become_a_column_separator()
    {
        // Message payloads are arbitrary bytes. A raw tab puts the rest of the value in the next
        // field, so `cut -f2` returns half a value on one row and the whole one on the next.
        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(new CommandResult.Table(
            null, new[] { "KEY", "VALUE" },
            new IReadOnlyList<string>[] { new[] { "k1", "left\tright" }, new[] { "k2", "plain" } }));

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lines, line => Assert.Equal(1, line.Count(c => c == '\t')));
        Assert.Contains(@"left\tright", output.ToString());
    }

    [Fact]
    public void A_newline_inside_a_value_does_not_become_a_second_record()
    {
        // A multi-line JSON payload would print as extra lines that any line-oriented reader counts
        // as further records - silently inflating how many messages came back.
        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(new CommandResult.Table(
            null, new[] { "KEY", "VALUE" },
            new IReadOnlyList<string>[] { new[] { "k1", "line1\nline2\r\nline3" } }));

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);   // header + exactly one record
        Assert.Contains(@"line1\nline2\r\nline3", output.ToString());
    }

    [Fact]
    public void Pairs_values_are_escaped_the_same_way()
    {
        var output = new StringWriter();
        new PlainTextRenderer(output, new StringWriter()).Render(
            new CommandResult.Pairs(null, new Dictionary<string, string> { ["k"] = "a\tb" }));

        Assert.Equal(1, output.ToString().TrimEnd('\n').Count(c => c == '\t'));
    }

    [Fact]
    public void SpectreRenderer_does_not_crash_on_a_row_that_does_not_match_the_columns()
    {
        // A command building a malformed table is a programming bug, but the renderer runs outside
        // the dispatcher's exception boundary - throwing here takes down the whole REPL.
        var record = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(record)
        });

        var ex = Record.Exception(() => new SpectreRenderer(console).Render(new CommandResult.Table(
            null, new[] { "A", "B" },
            new IReadOnlyList<string>[] { new[] { "1", "2", "3", "4" }, new[] { "only" } })));

        Assert.Null(ex);
    }
}
