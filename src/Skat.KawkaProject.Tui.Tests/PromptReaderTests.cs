using Spectre.Console;
using Skat.KawkaProject.Tui.Input;

namespace Skat.KawkaProject.Tui.Tests;

public class PromptReaderTests
{
    private sealed class ScriptedKeys(params ConsoleKeyInfo[] keys) : IKeySource
    {
        private int _i;
        public ConsoleKeyInfo ReadKey() => _i < keys.Length ? keys[_i++]
            : new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
    }

    private static ConsoleKeyInfo Ch(char c) => new(c, ConsoleKey.A, false, false, false);
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);
    private static ConsoleKeyInfo CtrlD() => new('', ConsoleKey.D, false, false, control: true);

    /// <summary>
    /// ANSI on, colours off. The prompt moves the cursor, and with ANSI disabled Spectre falls back
    /// to the legacy cursor, which drives the real Console and throws when there is no console -
    /// so the No-ANSI console would test the fallback rather than the code under test.
    /// </summary>
    private static IAnsiConsole SilentConsole(StringWriter? sink = null) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Yes,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(sink ?? new StringWriter())
    });

    private static PromptReader Reader(LineHistory history, params ConsoleKeyInfo[] keys) =>
        new(new ScriptedKeys(keys), history, SilentConsole());

    [Fact]
    public void Typed_characters_become_the_line()
    {
        var line = Reader(new LineHistory(), Ch('h'), Ch('i'), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("hi", line);
    }

    [Fact]
    public void Backspace_removes_the_last_character()
    {
        var line = Reader(new LineHistory(), Ch('a'), Ch('b'), Key(ConsoleKey.Backspace), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("a", line);
    }

    [Fact]
    public void Backspace_on_an_empty_line_does_nothing()
    {
        var line = Reader(new LineHistory(), Key(ConsoleKey.Backspace), Ch('a'), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("a", line);
    }

    [Fact]
    public void Up_arrow_recalls_the_previous_command()
    {
        var history = new LineHistory();
        history.Add("topics");

        var line = Reader(history, Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("topics", line);
    }

    [Fact]
    public void A_recalled_command_can_be_edited_before_running()
    {
        // The point of history: recall `describe orders`, fix the topic, run it.
        var history = new LineHistory();
        history.Add("topics");

        var line = Reader(history,
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Backspace), Ch('!'), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("topic!", line);
    }

    [Fact]
    public void Down_arrow_walks_back_towards_the_empty_line()
    {
        var history = new LineHistory();
        history.Add("a"); history.Add("b");

        var line = Reader(history,
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("b", line);
    }

    [Fact]
    public void Escape_clears_what_was_typed()
    {
        var line = Reader(new LineHistory(), Ch('o'), Ch('o'), Ch('p'), Key(ConsoleKey.Escape), Ch('x'), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("x", line);
    }

    [Fact]
    public void Submitted_lines_are_added_to_history()
    {
        var history = new LineHistory();

        Reader(history, Ch('x'), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("x", history.Previous());
    }

    [Fact]
    public void CtrlD_on_an_empty_line_returns_null()
    {
        Assert.Null(Reader(new LineHistory(), CtrlD()).ReadLine("> "));
    }

    [Fact]
    public void CtrlD_with_text_typed_does_not_end_the_session()
    {
        // Ctrl+D means "end of input", not "throw away what I am in the middle of writing".
        var line = Reader(new LineHistory(), Ch('a'), CtrlD(), Ch('b'), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("ab", line);
    }

    [Fact]
    public void Control_characters_never_reach_the_buffer()
    {
        // A stray tab or bell would be stored verbatim and then written to the history file, where
        // it corrupts the one-entry-per-line format.
        var line = Reader(new LineHistory(),
            Ch('a'), Ch('\t'), Ch(''), Ch('b'), Key(ConsoleKey.Enter)).ReadLine("> ");

        Assert.Equal("ab", line);
    }

    [Fact]
    public void The_prompt_is_redrawn_in_place_rather_than_stacked()
    {
        // Every keystroke redraws. Writing a fresh block each time would leave one copy of the
        // prompt per character typed marching down the screen.
        var sink = new StringWriter();
        var reader = new PromptReader(
            new ScriptedKeys(Ch('a'), Ch('b'), Ch('c'), Key(ConsoleKey.Enter)),
            new LineHistory(), SilentConsole(sink));

        reader.ReadLine("> ");

        var text = sink.ToString();
        Assert.Equal(1, text.Count(c => c == '\n'));   // just the newline that ends the line
    }
}
