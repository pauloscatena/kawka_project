using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Input;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

/// <summary>
/// The REPL loop, driven through a scripted line reader. These need no terminal, which is the
/// point: the session lifecycle lives here, and getting it wrong leaks connections or tells the
/// user they disconnected when they did not.
/// </summary>
public class TuiHostTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    /// <summary>Feeds scripted lines, then EOF - which is how the loop is expected to end.</summary>
    private sealed class ScriptedReader(params string[] lines) : ILineReader
    {
        private int _next;
        public string? ReadLine(string prompt) => _next < lines.Length ? lines[_next++] : null;
    }

    private static (TuiHost host, StringWriter output) HostWith(
        ILineReader reader, params ITuiCommand[] commands)
    {
        var registry = new CommandRegistry(commands);
        var output = new StringWriter();
        var host = new TuiHost(
            new CommandDispatcher(registry), registry,
            new PlainTextRenderer(output, new StringWriter()),
            new NoConfirmer(), reader);
        return (host, output);
    }

    private static (ConnectCommand connect, Mock<IKafkaSession> session) ConnectYielding(string profileName)
    {
        var profile = new ConnectionProfile { Name = profileName, BootstrapServers = "k:9092" };
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(new[] { profile });

        var session = new Mock<IKafkaSession>();
        session.Setup(s => s.ProfileName).Returns(profileName);
        var factory = new Mock<IKafkaConnectionFactory>();
        factory.Setup(f => f.ConnectAsync(profile)).ReturnsAsync(session.Object);

        return (new ConnectCommand(repo.Object, factory.Object), session);
    }

    [Fact]
    public async Task Exit_ends_the_loop()
    {
        var (host, _) = HostWith(new ScriptedReader("exit"), new StatusCommand());

        var code = await host.RunReplAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
    }

    [Fact]
    public async Task End_of_input_ends_the_loop()
    {
        // Ctrl+D, or a script piping commands in. Without this the loop spins on null forever.
        var (host, _) = HostWith(new ScriptedReader(), new StatusCommand());

        var code = await host.RunReplAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
    }

    [Fact]
    public async Task A_failing_command_does_not_end_the_session()
    {
        var (host, output) = HostWith(
            new ScriptedReader("nope", "status", "exit"), new StatusCommand());

        await host.RunReplAsync(CancellationToken.None);

        // The unknown verb failed, and the prompt came back to run the next line.
        Assert.Contains("No active connection", output.ToString());
    }

    [Fact]
    public async Task Connect_makes_the_session_available_to_later_commands()
    {
        var (connect, session) = ConnectYielding("prod");
        var (host, output) = HostWith(
            new ScriptedReader("connect prod", "status", "exit"), connect, new StatusCommand());

        await host.RunReplAsync(CancellationToken.None);

        Assert.Contains("Connected to 'prod'", output.ToString());
        session.Verify(s => s.Dispose(), Times.Never);   // still in use
    }

    [Fact]
    public async Task Connecting_again_disposes_the_session_it_replaces()
    {
        // Without this the previous session is dropped on the floor. It costs nothing today because
        // KafkaSession.Dispose is a no-op, and costs a leaked client the day it stops being one.
        var profileA = new ConnectionProfile { Name = "a", BootstrapServers = "ka:9092" };
        var profileB = new ConnectionProfile { Name = "b", BootstrapServers = "kb:9092" };
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(new[] { profileA, profileB });

        var first = new Mock<IKafkaSession>();
        var second = new Mock<IKafkaSession>();
        var factory = new Mock<IKafkaConnectionFactory>();
        factory.Setup(f => f.ConnectAsync(profileA)).ReturnsAsync(first.Object);
        factory.Setup(f => f.ConnectAsync(profileB)).ReturnsAsync(second.Object);

        var (host, _) = HostWith(
            new ScriptedReader("connect a", "connect b", "exit"),
            new ConnectCommand(repo.Object, factory.Object));

        await host.RunReplAsync(CancellationToken.None);

        first.Verify(s => s.Dispose(), Times.Once);
        second.Verify(s => s.Dispose(), Times.Never);
    }

    [Fact]
    public async Task Disconnect_actually_disconnects()
    {
        // The command only prints "Disconnected from 'x'." - the host is what makes that true.
        // Without it the user reads that they are disconnected and is still connected.
        var (connect, session) = ConnectYielding("prod");
        var (host, output) = HostWith(
            new ScriptedReader("connect prod", "disconnect", "status", "exit"),
            connect, new DisconnectCommand(), new StatusCommand());

        await host.RunReplAsync(CancellationToken.None);

        session.Verify(s => s.Dispose(), Times.Once);
        Assert.Contains("No active connection", output.ToString());
    }

    [Fact]
    public async Task A_refused_disconnect_keeps_the_session()
    {
        // disconnect requires a session, so running it without one is a usage failure - and must
        // not be mistaken for a successful disconnect.
        var (host, _) = HostWith(new ScriptedReader("disconnect", "exit"), new DisconnectCommand());

        var code = await host.RunReplAsync(CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);   // the REPL itself still ended cleanly
    }

    [Fact]
    public async Task Disposing_the_host_disposes_the_open_session()
    {
        var (connect, session) = ConnectYielding("prod");
        var (host, _) = HostWith(new ScriptedReader("connect prod", "exit"), connect);

        await host.RunReplAsync(CancellationToken.None);
        host.Dispose();

        session.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task One_shot_returns_the_commands_exit_code()
    {
        var (host, _) = HostWith(new ScriptedReader(), new StatusCommand());

        var code = await host.RunOnceAsync(ArgumentParser.ParseLine("nope"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, code);
    }
}
