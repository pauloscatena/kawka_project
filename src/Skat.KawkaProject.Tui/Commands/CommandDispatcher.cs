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

        try
        {
            // Inside the try, not before it: a command's metadata is still the command's code, and
            // reading it outside would let a throwing getter escape the one boundary that exists.
            if (command.RequiresSession && session is null)
                return new CommandResult.Failure(
                    "No active connection. Use 'connect <profile>' first.", ExitCodes.Usage);

            return await command.ExecuteAsync(
                new CommandContext { Parsed = parsed, Session = session, Confirmer = confirmer }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Guarded on the token: TaskCanceledException derives from this, so an unguarded catch
            // would report a driver timeout as "Cancelled." - telling the operator they pressed
            // Ctrl+C when they did not, and discarding the only clue about what actually timed out.
            return new CommandResult.Failure("Cancelled.", ExitCodes.Cancelled);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            // Argument problems are the user's typo, not the cluster's fault: usage, with the
            // command's own usage line. ArgumentOutOfRangeException is covered by ArgumentException.
            return new CommandResult.Failure($"{ex.Message}{UsageHint(command)}", ExitCodes.Usage);
        }
        catch (Exception ex)
        {
            return new CommandResult.Failure(ex.Message, ExitCodes.OperationalFailure);
        }
    }

    /// <summary>
    /// The usage line, or nothing if the command cannot produce one.
    /// </summary>
    /// <remarks>
    /// This runs while reporting someone else's error. A throwing Usage getter must not replace the
    /// mistake the user actually made with an internal crash - the failure path destroying the
    /// failure it was asked to describe.
    /// </remarks>
    private static string UsageHint(ITuiCommand command)
    {
        try { return $"\nUsage: {command.Usage}"; }
        catch { return string.Empty; }
    }
}
