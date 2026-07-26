using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Input;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui;

/// <summary>
/// Owns the session lifecycle and the REPL loop. Commands never dispose the session:
/// it belongs to the host, which is the only thing that outlives a single command.
/// </summary>
/// <remarks>
/// Commands are dispatched one at a time, and that is load-bearing rather than incidental:
/// ConnectCommand reports the session it opened through a property the host reads immediately
/// after the await. Dispatching concurrently would race two connects against one another and hand
/// the host whichever session finished last.
/// </remarks>
public sealed class TuiHost(
    CommandDispatcher dispatcher,
    CommandRegistry registry,
    IResultRenderer renderer,
    IConfirmer confirmer,
    ILineReader input) : IDisposable
{
    private IKafkaSession? _session;
    private CancellationTokenSource? _runningCommand;

    public async Task<int> RunOnceAsync(ParsedCommand parsed, CancellationToken ct)
    {
        var result = await dispatcher.DispatchAsync(parsed, _session, confirmer, ct);
        AdoptSessionIfConnected(parsed);
        HandleDisconnect(parsed, result);
        renderer.Render(result);
        return result.ExitCodeOrSuccess;
    }

    /// <summary>
    /// Runs a command and keeps its output to itself, returning only the exit code.
    /// </summary>
    /// <remarks>
    /// For the implicit connect that one-shot mode performs before the real command. Printing
    /// "Connected to 'prod'." would put a line into stdout that the user never asked for, ahead of
    /// the output they did - and straight into whatever is on the other side of the pipe. A failure
    /// is still rendered: silence there would leave them staring at an unexplained non-zero exit.
    /// </remarks>
    public async Task<int> RunQuietlyAsync(ParsedCommand parsed, CancellationToken ct)
    {
        var result = await dispatcher.DispatchAsync(parsed, _session, confirmer, ct);
        AdoptSessionIfConnected(parsed);
        if (result is CommandResult.Failure) renderer.Render(result);
        return result.ExitCodeOrSuccess;
    }

    /// <summary>
    /// Cancels the command currently executing, if any. Returns false when the prompt is idle,
    /// which is the caller's signal that Ctrl+C should exit instead of cancelling.
    /// </summary>
    public bool CancelRunningCommand()
    {
        var running = _runningCommand;
        if (running is null || running.IsCancellationRequested) return false;
        running.Cancel();
        return true;
    }

    public async Task<int> RunReplAsync(CancellationToken ct)
    {
        renderer.Render(new CommandResult.Text("kawka · type 'help' for commands, 'exit' to quit"));

        while (!ct.IsCancellationRequested)
        {
            var line = input.ReadLine("> ");
            if (line is null) break;                                  // EOF (Ctrl+D)
            if (line.Trim() is "exit" or "quit") break;

            var parsed = ArgumentParser.ParseLine(line);

            // A fresh token per command, linked to the global one: Ctrl+C during a command
            // cancels only that command and the loop keeps going. A failed or cancelled
            // command never kills the REPL.
            using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runningCommand = commandCts;
            try
            {
                var result = await dispatcher.DispatchAsync(parsed, _session, confirmer, commandCts.Token);
                AdoptSessionIfConnected(parsed);
                HandleDisconnect(parsed, result);
                renderer.Render(result);
            }
            finally
            {
                _runningCommand = null;
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>Takes ownership of a session created by ConnectCommand, disposing any previous one.</summary>
    private void AdoptSessionIfConnected(ParsedCommand parsed)
    {
        if (registry.Resolve(parsed.Verb) is not ConnectCommand connect) return;
        if (connect.Established is null) return;

        _session?.Dispose();
        _session = connect.Established;
    }

    /// <summary>
    /// Makes the disconnect command's message true. It only reports; the session belongs here.
    /// </summary>
    private void HandleDisconnect(ParsedCommand parsed, CommandResult result)
    {
        if (parsed.Verb.Equals("disconnect", StringComparison.OrdinalIgnoreCase)
            && result is not CommandResult.Failure)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    public void Dispose() => _session?.Dispose();
}
