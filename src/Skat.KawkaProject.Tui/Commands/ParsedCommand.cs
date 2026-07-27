namespace Skat.KawkaProject.Tui.Commands;

public sealed record ParsedCommand(
    string Verb,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string?> Flags)
{
    public string? Flag(string name) => Flags.TryGetValue(name, out var v) ? v : null;
    public bool HasFlag(string name) => Flags.ContainsKey(name);

    /// <summary>Reads a flag as int. Returns null when absent, throws FormatException when unparseable.</summary>
    public int? IntFlag(string name)
    {
        var raw = Flag(name);
        if (raw is null) return null;
        if (!int.TryParse(raw, out var value))
            throw new FormatException($"--{name} expects a number, got '{raw}'.");
        return value;
    }
}
