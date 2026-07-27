namespace Skat.KawkaProject.Tui.Commands;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, ITuiCommand> _byName;

    /// <summary>
    /// Verbs resolve case-insensitively, matching how the parser treats flag names. Without that,
    /// <c>--OUTPUT</c> would work while <c>TOPICS</c> came back "unknown command" - an asymmetry
    /// nobody can guess from the outside.
    /// </summary>
    /// <remarks>
    /// ToDictionary throws on a duplicate name. That is deliberate: two commands claiming one verb
    /// is a composition bug, and failing at startup beats having one silently shadow the other
    /// depending on registration order.
    /// </remarks>
    public CommandRegistry(IEnumerable<ITuiCommand> commands)
    {
        _byName = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ITuiCommand> All => _byName.Values;

    public ITuiCommand? Resolve(string verb) =>
        _byName.TryGetValue(verb, out var cmd) ? cmd : null;
}
