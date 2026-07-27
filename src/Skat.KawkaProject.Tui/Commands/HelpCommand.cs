namespace Skat.KawkaProject.Tui.Commands;

/// <param name="registry">
/// Resolved lazily because help is itself a registered command: the registry cannot be built until
/// every command exists, and this command cannot be built until the registry does. Taking a factory
/// breaks the cycle, and is why help appears in its own listing - the one command a new user is
/// certain to try.
/// </param>
public sealed class HelpCommand(Func<CommandRegistry> registry) : ITuiCommand
{
    public string Name => "help";
    public string Usage => "help [command]";
    public string Summary => "Show available commands, or details of one";
    public bool RequiresSession => false;

    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var commands = registry();

        if (ctx.Parsed.Args.Count > 0)
        {
            var name = ctx.Parsed.Args[0];
            var cmd = commands.Resolve(name);
            return Task.FromResult<CommandResult>(cmd is null
                ? new CommandResult.Failure($"Unknown command '{name}'.", ExitCodes.Usage)
                : new CommandResult.Text($"{cmd.Usage}\n  {cmd.Summary}"));
        }

        // Sorted here, deliberately: the registry hands back registration order, which is an
        // accident of how the composition root happens to be written.
        var rows = commands.All
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => (IReadOnlyList<string>)new[] { c.Usage, c.Summary })
            .ToList();

        return Task.FromResult<CommandResult>(
            new CommandResult.Table("Commands", new[] { "USAGE", "WHAT IT DOES" }, rows));
    }
}
