using Skat.KawkaProject.Core.Models;   // DestructiveAction

namespace Skat.KawkaProject.Tui.Safety;

/// <summary>
/// Asks the user to approve a destructive operation. Implementations land in Phase 4: one that
/// makes an interactive user type the topic name, and one that refuses by default when there is no
/// TTY to ask.
/// </summary>
/// <remarks>
/// <see cref="DestructiveAction"/> is NOT declared here. It lives in Core as the single home for
/// what a destructive operation destroys and keeps, and the GUI already reads from it - a second
/// copy would be exactly the divergence that centralising it closed.
/// </remarks>
public interface IConfirmer
{
    Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct);
}
