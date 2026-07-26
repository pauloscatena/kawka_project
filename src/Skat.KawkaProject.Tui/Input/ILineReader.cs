namespace Skat.KawkaProject.Tui.Input;

/// <summary>
/// Reads one line of input from the user. Returns null at end of input (Ctrl+D, or a script piping
/// commands in), which is the REPL's signal to stop.
/// </summary>
/// <remarks>
/// An abstraction rather than a direct Console.ReadLine so that the host stays testable and the
/// terminal stays confined to Input/ and Rendering/ - the host owns the session lifecycle, and that
/// is worth testing without a TTY. Phase 2 swaps the implementation for the bordered prompt with
/// history and arrow keys; nothing else has to change.
/// </remarks>
public interface ILineReader
{
    string? ReadLine(string prompt);
}
