using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class ProfilesCommand(IConnectionProfileRepository repo) : ITuiCommand
{
    public string Name => "profiles";
    public string Usage => "profiles";
    public string Summary => "List saved connection profiles";
    public bool RequiresSession => false;

    /// <remarks>
    /// Name, servers and auth type only. The profile store also holds SASL credentials and key
    /// paths; echoing those would put a password into scrollback, into `kawka profiles > file`, and
    /// into any screen share. If a column is ever added here, it must not be one of those.
    /// </remarks>
    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var rows = repo.GetAll()
            .Select(p => (IReadOnlyList<string>)new[] { p.Name, p.BootstrapServers, p.AuthType.ToString() })
            .ToList();

        return Task.FromResult<CommandResult>(
            new CommandResult.Table("Profiles", new[] { "NAME", "BOOTSTRAP", "AUTH" }, rows));
    }
}

public sealed class ConnectCommand(IConnectionProfileRepository repo, IKafkaConnectionFactory factory) : ITuiCommand
{
    public string Name => "connect";
    public string Usage => "connect <profile>";
    public string Summary => "Open a session against a saved profile";
    public bool RequiresSession => false;

    /// <summary>Set on success so the host can take ownership of the new session.</summary>
    /// <remarks>
    /// Cleared at the top of every run. Leaving a previous value behind after a failed connect
    /// would have the host keep handing commands a session the user believes is gone - and one
    /// belonging to a profile they did not ask for.
    /// </remarks>
    public IKafkaSession? Established { get; private set; }

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        Established = null;

        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing profile name. Usage: {Usage}", ExitCodes.Usage);

        var name = ctx.Parsed.Args[0];
        var all = repo.GetAll();
        var profile = all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            var known = all.Count == 0 ? "(none saved)" : string.Join(", ", all.Select(p => p.Name));
            return new CommandResult.Failure($"No profile named '{name}'. Available: {known}", ExitCodes.Usage);
        }

        Established = await factory.ConnectAsync(profile);
        return new CommandResult.Text($"Connected to '{profile.Name}' ({profile.BootstrapServers}).");
    }
}

/// <remarks>
/// Only reports. The session's lifetime belongs to the host, which owns it and disposes it - a
/// command disposing something it did not create would leave the host holding a dead session.
/// </remarks>
public sealed class DisconnectCommand : ITuiCommand
{
    public string Name => "disconnect";
    public string Usage => "disconnect";
    public string Summary => "Close the active session";
    public bool RequiresSession => true;

    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct) =>
        Task.FromResult<CommandResult>(new CommandResult.Text($"Disconnected from '{ctx.RequireSession().ProfileName}'."));
}

public sealed class StatusCommand : ITuiCommand
{
    public string Name => "status";
    public string Usage => "status";
    public string Summary => "Show the active connection";
    public bool RequiresSession => false;

    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct) =>
        Task.FromResult<CommandResult>(ctx.Session is null
            ? new CommandResult.Text("No active connection.")
            : new CommandResult.Text($"Connected to '{ctx.Session.ProfileName}' ({ctx.Session.BootstrapServers})."));
}
