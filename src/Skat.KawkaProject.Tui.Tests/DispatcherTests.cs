using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class DispatcherTests
{
    private sealed class StubCommand(string name, bool requiresSession, Func<CommandContext, Task<CommandResult>> body) : ITuiCommand
    {
        public string Name => name;
        public string Usage => $"{name}";
        public string Summary => "stub";
        public bool RequiresSession => requiresSession;
        public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct) => body(ctx);
    }

    private sealed class StubConfirmer(bool answer) : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct) => Task.FromResult(answer);
    }

    private static readonly IConfirmer AlwaysYes = new StubConfirmer(true);

    private static CommandDispatcher DispatcherWith(params ITuiCommand[] commands) =>
        new(new CommandRegistry(commands));

    [Fact]
    public async Task Unknown_verb_is_a_usage_failure()
    {
        var result = await DispatcherWith().DispatchAsync(
            ArgumentParser.ParseLine("nope"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
        Assert.Contains("nope", failure.Message);
    }

    [Fact]
    public async Task Command_needing_a_session_fails_cleanly_when_there_is_none()
    {
        var cmd = new StubCommand("topics", true, _ => Task.FromResult<CommandResult>(new CommandResult.Text("ok")));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("topics"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
        Assert.Contains("connect", failure.Message);
    }

    [Fact]
    public async Task Exceptions_from_commands_become_operational_failures()
    {
        var cmd = new StubCommand("boom", false, _ => throw new InvalidOperationException("broker unreachable"));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("boom"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.OperationalFailure, failure.ExitCode);
        Assert.Contains("broker unreachable", failure.Message);
    }

    [Fact]
    public async Task Bad_flag_format_is_a_usage_failure_not_an_operational_one()
    {
        var cmd = new StubCommand("n", false, ctx => Task.FromResult<CommandResult>(
            new CommandResult.Text(ctx.Parsed.IntFlag("to")!.Value.ToString())));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("n --to abc"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
    }

    [Fact]
    public async Task Empty_input_is_a_no_op()
    {
        var result = await DispatcherWith().DispatchAsync(
            ArgumentParser.ParseLine("  "), null, AlwaysYes, CancellationToken.None);

        Assert.IsType<CommandResult.Text>(result);
    }

    [Fact]
    public async Task A_verb_resolves_regardless_of_case()
    {
        // Flags are matched case-insensitively by the parser. If the verb were not, --OUTPUT would
        // work while TOPICS did not - an asymmetry nobody can guess from the outside.
        var cmd = new StubCommand("topics", false, _ => Task.FromResult<CommandResult>(new CommandResult.Text("ok")));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("TOPICS"), null, AlwaysYes, CancellationToken.None);

        Assert.IsType<CommandResult.Text>(result);
    }

    [Fact]
    public async Task A_cancelled_command_is_reported_as_cancelled_not_as_a_crash()
    {
        var cmd = new StubCommand("slow", false, _ => throw new OperationCanceledException());

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("slow"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Contains("Cancel", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_usage_failure_shows_the_command_its_own_usage_line()
    {
        // The point of routing argument errors separately: the user gets told how to call the
        // command, not just that a number failed to parse.
        var cmd = new StubCommand("consume", false, ctx => Task.FromResult<CommandResult>(
            new CommandResult.Text(ctx.Parsed.IntFlag("limit")!.Value.ToString())));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("consume --limit nope"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Contains("consume", failure.Message);
        Assert.Contains("limit", failure.Message);
    }

    [Fact]
    public async Task A_command_that_does_not_need_a_session_runs_without_one()
    {
        var cmd = new StubCommand("profiles", false, _ => Task.FromResult<CommandResult>(new CommandResult.Text("listed")));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("profiles"), null, AlwaysYes, CancellationToken.None);

        Assert.Equal("listed", Assert.IsType<CommandResult.Text>(result).Message);
    }

    private sealed class ThrowingMetadataCommand(string name, bool throwOnUsage) : ITuiCommand
    {
        public string Name => name;
        public string Usage => throwOnUsage
            ? throw new InvalidOperationException("Usage getter blew up")
            : name;
        public string Summary => "stub";
        public bool RequiresSession => throwOnUsage
            ? false
            : throw new InvalidOperationException("RequiresSession getter blew up");
        public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct) =>
            throw new FormatException("the original problem");
    }

    [Fact]
    public async Task A_throwing_RequiresSession_does_not_escape_the_dispatcher()
    {
        // The dispatcher is documented as the only place that catches Exception. Reading command
        // metadata outside its try makes that false, and the REPL has nothing above it to catch.
        var result = await DispatcherWith(new ThrowingMetadataCommand("bad", throwOnUsage: false))
            .DispatchAsync(ArgumentParser.ParseLine("bad"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Contains("RequiresSession getter blew up", failure.Message);
    }

    [Fact]
    public async Task A_throwing_Usage_does_not_lose_the_error_being_reported()
    {
        // Usage is read while formatting the usage failure. If that getter throws, the user's actual
        // mistake is replaced by an internal crash - the failure path destroying the failure.
        var result = await DispatcherWith(new ThrowingMetadataCommand("bad", throwOnUsage: true))
            .DispatchAsync(ArgumentParser.ParseLine("bad"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Contains("the original problem", failure.Message);
    }

    [Fact]
    public async Task A_timeout_that_is_not_the_users_cancellation_keeps_its_own_message()
    {
        // TaskCanceledException derives from OperationCanceledException, so an HTTP or driver
        // timeout inside a command would be reported as "Cancelled." - telling the operator they
        // pressed Ctrl+C when they did not, and throwing away the only clue about what timed out.
        var cmd = new StubCommand("slow", false,
            _ => throw new TaskCanceledException("metadata request timed out"));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("slow"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Contains("timed out", failure.Message);
        Assert.Equal(ExitCodes.OperationalFailure, failure.ExitCode);
    }

    [Fact]
    public async Task A_real_cancellation_exits_with_its_own_code()
    {
        // Ctrl+C is not an operational failure. A script retrying on exit code 1 must not retry
        // something the operator deliberately stopped.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cmd = new StubCommand("slow", false, _ => throw new OperationCanceledException());

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("slow"), null, AlwaysYes, cts.Token);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Cancelled, failure.ExitCode);
        Assert.Contains("Cancel", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_commands_claiming_the_same_name_is_caught_when_the_registry_is_built()
    {
        // Composition-time bug. Better to fail loudly at startup than to have one command silently
        // shadow another depending on registration order.
        var ex = Record.Exception(() => new CommandRegistry(new ITuiCommand[]
        {
            new StubCommand("topics", false, _ => Task.FromResult<CommandResult>(new CommandResult.Text("a"))),
            new StubCommand("TOPICS", false, _ => Task.FromResult<CommandResult>(new CommandResult.Text("b"))),
        }));

        Assert.NotNull(ex);
    }
}
