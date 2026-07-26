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

    /// <param name="Values">
    /// Rendered in iteration order, which for a plain Dictionary is insertion order in practice but
    /// is not a documented contract. Whoever builds a Pairs owns the order the user sees: insert in
    /// the order that reads well, or pass an ordered implementation. Do not rely on the renderer to
    /// sort - it deliberately does not, because alphabetical is rarely the useful order for status
    /// output.
    /// </param>
    public sealed record Pairs(string? Title, IReadOnlyDictionary<string, string> Values) : CommandResult;

    public sealed record Text(string Message) : CommandResult;

    public sealed record Failure(string Message, int ExitCode) : CommandResult;

    /// <summary>
    /// Several results from one command, rendered in order.
    /// </summary>
    /// <remarks>
    /// Exists so a command with two shapes of fact - describe has topic-level values and a table of
    /// partitions - can return both as data. Without it the only way to carry the second fact was to
    /// write it into a title, which the plain-text renderer drops by design, so it vanished the
    /// moment anyone piped the command.
    /// <para>
    /// Must not contain itself. Parts is not copied defensively, so a caller that keeps a reference
    /// to the list it passed in could add the group to itself - and both the renderers and
    /// ExitCodeOrSuccess recurse, so the result is a StackOverflowException, which cannot be caught.
    /// Build the list, then hand it over.
    /// </para>
    /// </remarks>
    public sealed record Group(IReadOnlyList<CommandResult> Parts) : CommandResult;

    /// <summary>
    /// The exit code this result implies. A Group takes the first failing part's code: a command
    /// that produced any failure did not succeed, whatever else it also produced.
    /// </summary>
    public int ExitCodeOrSuccess => this switch
    {
        Failure f => f.ExitCode,
        Group g => g.Parts.Select(p => p.ExitCodeOrSuccess)
                          .FirstOrDefault(code => code != ExitCodes.Success, ExitCodes.Success),
        _ => ExitCodes.Success
    };
}
