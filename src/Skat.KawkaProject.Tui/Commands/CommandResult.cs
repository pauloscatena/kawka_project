namespace Skat.KawkaProject.Tui.Commands;

/// <summary>
/// What a command produces: data, never pixels. Rendering is a separate layer, which is what lets
/// almost the whole suite run without a terminal.
/// </summary>
public abstract record CommandResult
{
    public sealed record Table(
        string? Title,
        IReadOnlyList<string> Columns,
        IReadOnlyList<IReadOnlyList<string>> Rows) : CommandResult;

    public sealed record Pairs(string? Title, IReadOnlyDictionary<string, string> Values) : CommandResult;

    public sealed record Text(string Message) : CommandResult;

    public sealed record Failure(string Message, int ExitCode) : CommandResult;

    public int ExitCodeOrSuccess => this is Failure f ? f.ExitCode : ExitCodes.Success;
}
