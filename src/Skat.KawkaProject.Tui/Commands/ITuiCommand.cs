namespace Skat.KawkaProject.Tui.Commands;

public interface ITuiCommand
{
    string Name { get; }
    string Usage { get; }
    string Summary { get; }

    /// <summary>When true the dispatcher short-circuits with a usage failure if no session is open,
    /// so no handler needs to null-check the session.</summary>
    bool RequiresSession { get; }

    Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct);
}
