using System.Text;
using Spectre.Console;

namespace Skat.KawkaProject.Tui.Input;

/// <summary>
/// The interactive prompt: in-line editing, history on the arrow keys, Ctrl+D to end the session.
/// Spectre's TextPrompt reads a whole line at once, so this owns the key loop, the edit buffer and
/// the redraw. Keys arrive through IKeySource so tests can script them without a terminal.
/// </summary>
/// <remarks>
/// Redraws the current line in place with a carriage return, rather than writing a bordered panel
/// per keystroke. A panel cannot be un-drawn without multi-line cursor control, so that version
/// would leave one box per character typed marching down the screen. A single line that edits
/// correctly is worth more than a border that smears.
/// <para>
/// Requires a real terminal: <see cref="ConsoleKeySource"/> throws when stdin is redirected, so the
/// composition root selects <see cref="ConsoleLineReader"/> instead in that case.
/// </para>
/// </remarks>
public sealed class PromptReader(IKeySource keys, LineHistory history, IAnsiConsole console) : ILineReader
{
    /// <summary>Reads one line. Returns null on Ctrl+D at an empty prompt (EOF).</summary>
    public string? ReadLine(string prompt)
    {
        var buffer = new StringBuilder();
        Redraw(prompt, buffer.ToString());

        while (true)
        {
            var key = keys.ReadKey();

            // Ctrl+D ends the session, but only when there is nothing half-typed: it means "no more
            // input", not "discard what I am writing".
            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (buffer.Length == 0)
                {
                    console.WriteLine();
                    return null;
                }
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                {
                    var line = buffer.ToString();
                    history.Add(line);
                    console.WriteLine();
                    return line;
                }

                case ConsoleKey.Backspace:
                    if (buffer.Length > 0) buffer.Length--;
                    break;

                case ConsoleKey.UpArrow:
                    buffer.Clear().Append(history.Previous());
                    break;

                case ConsoleKey.DownArrow:
                    buffer.Clear().Append(history.Next());
                    break;

                case ConsoleKey.Escape:
                    buffer.Clear();
                    history.ResetCursor();
                    break;

                default:
                    // Control characters are dropped rather than stored: a stray tab or bell would
                    // survive into the history file and break its one-entry-per-line format.
                    if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
                    break;
            }

            Redraw(prompt, buffer.ToString());
        }
    }

    private void Redraw(string prompt, string current)
    {
        // Cursor movement rather than a carriage return: Spectre normalises '\r' into a line break,
        // so the CR version scrolled a fresh copy of the prompt for every keystroke.
        var line = prompt + current;

        if (_lastDrawnLength > 0) console.Cursor.MoveLeft(_lastDrawnLength);

        // Pad over whatever a previous, longer line left behind, then step back onto the text -
        // without it, deleting a character leaves its ghost on screen.
        var overhang = Math.Max(0, _lastDrawnLength - line.Length);
        console.Write(line + new string(' ', overhang));
        if (overhang > 0) console.Cursor.MoveLeft(overhang);

        _lastDrawnLength = line.Length;
    }

    private int _lastDrawnLength;
}
