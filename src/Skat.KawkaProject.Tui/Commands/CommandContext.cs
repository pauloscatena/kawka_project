using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class CommandContext
{
    public required ParsedCommand Parsed { get; init; }
    public required IConfirmer Confirmer { get; init; }

    /// <summary>Null when no connection is open. Commands with RequiresSession never see null.</summary>
    public IKafkaSession? Session { get; init; }

    public IKafkaSession RequireSession() => Session
        ?? throw new InvalidOperationException("Session missing; RequiresSession should have short-circuited.");
}
