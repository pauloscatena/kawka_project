namespace Skat.KawkaProject.Tui.Input;

/// <summary>Plain Console.ReadLine. Phase 2 replaces this with the bordered prompt.</summary>
public sealed class ConsoleLineReader : ILineReader
{
    public string? ReadLine(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine();
    }
}
