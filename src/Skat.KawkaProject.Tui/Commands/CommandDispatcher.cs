using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Commands;

/// <summary>
/// The ONLY place that catches Exception. Individual commands let exceptions propagate, which is
/// what keeps error messages consistent instead of every handler inventing its own format.
/// </summary>
public sealed class CommandDispatcher(CommandRegistry registry)
{
    public async Task<CommandResult> DispatchAsync(
        ParsedCommand parsed, IKafkaSession? session, IConfirmer confirmer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parsed.Verb)) return new CommandResult.Text("");

        var command = registry.Resolve(parsed.Verb);
        if (command is null)
            return new CommandResult.Failure(
                $"Unknown command '{parsed.Verb}'. Type 'help' to see what is available.", ExitCodes.Usage);

        if (command.RequiresSession && session is null)
            return new CommandResult.Failure(
                "No active connection. Use 'connect <profile>' first.", ExitCodes.Usage);

        try
        {
            return await command.ExecuteAsync(
                new CommandContext { Parsed = parsed, Session = session, Confirmer = confirmer }, ct);
        }
        catch (OperationCanceledException)
        {
            return new CommandResult.Failure("Cancelled.", ExitCodes.OperationalFailure);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or ArgumentException)
        {
            // Argument problems are the user's typo, not the cluster's fault: usage, with the command's own usage line.
            return new CommandResult.Failure($"{ex.Message}\nUsage: {command.Usage}", ExitCodes.Usage);
        }
        catch (Exception ex)
        {
            return new CommandResult.Failure(ex.Message, ExitCodes.OperationalFailure);
        }
    }
}
