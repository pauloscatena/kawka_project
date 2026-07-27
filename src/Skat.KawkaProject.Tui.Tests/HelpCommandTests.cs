using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class HelpCommandTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class Stub(string name) : ITuiCommand
    {
        public string Name => name;
        public string Usage => $"{name} <arg>";
        public string Summary => $"does {name}";
        public bool RequiresSession => false;
        public Task<CommandResult> ExecuteAsync(CommandContext c, CancellationToken ct) =>
            Task.FromResult<CommandResult>(new CommandResult.Text("ok"));
    }

    private static CommandContext Ctx(string line) => new()
    {
        Parsed = ArgumentParser.ParseLine(line), Confirmer = new NoConfirmer()
    };

    [Fact]
    public async Task Help_lists_every_registered_command()
    {
        var registry = new CommandRegistry(new ITuiCommand[] { new Stub("topics"), new Stub("describe") });

        var result = await new HelpCommand(() => registry).ExecuteAsync(Ctx("help"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public async Task Help_lists_itself()
    {
        // help is the one command a new user is certain to try. If it is missing from its own
        // listing, the tool looks like it has no help at all.
        HelpCommand? help = null;
        CommandRegistry? registry = null;
        help = new HelpCommand(() => registry!);
        registry = new CommandRegistry(new ITuiCommand[] { new Stub("topics"), help });

        var result = await help.ExecuteAsync(Ctx("help"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Contains(table.Rows, r => r[0].StartsWith("help", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Help_lists_commands_in_a_stable_order()
    {
        // The registry hands back registration order, which is an accident of the composition root.
        var registry = new CommandRegistry(new ITuiCommand[]
        {
            new Stub("topics"), new Stub("connect"), new Stub("describe")
        });

        var result = await new HelpCommand(() => registry).ExecuteAsync(Ctx("help"), CancellationToken.None);

        var usages = Assert.IsType<CommandResult.Table>(result).Rows.Select(r => r[0]).ToArray();
        Assert.Equal(new[] { "connect <arg>", "describe <arg>", "topics <arg>" }, usages);
    }

    [Fact]
    public async Task Help_for_one_command_shows_its_usage()
    {
        var registry = new CommandRegistry(new ITuiCommand[] { new Stub("topics") });

        var result = await new HelpCommand(() => registry).ExecuteAsync(Ctx("help topics"), CancellationToken.None);

        Assert.Contains("topics <arg>", Assert.IsType<CommandResult.Text>(result).Message);
    }

    [Fact]
    public async Task Help_for_an_unknown_command_is_a_usage_failure()
    {
        var registry = new CommandRegistry(Array.Empty<ITuiCommand>());

        var result = await new HelpCommand(() => registry).ExecuteAsync(Ctx("help nope"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }
}
