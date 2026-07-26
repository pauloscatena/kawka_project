namespace Skat.KawkaProject.Tui.Safety;

/// <summary>
/// Which confirmer a session gets. Pulled out of the composition root so it can be tested: getting
/// this wrong is the worst defect this project could ship, and it had no coverage while it lived
/// inside a registration lambda.
/// </summary>
public static class ConfirmerChoice
{
    /// <summary>
    /// True only when there is a human who can be asked to type the topic name.
    /// </summary>
    /// <remarks>
    /// Keyed on input redirection rather than on the renderer's TTY check: those answer different
    /// questions. Someone at a keyboard with stdout redirected to a file is still someone who can
    /// answer, and the renderer-based test would have silently refused every destructive operation
    /// for them. One-shot counts as script even on a terminal - there is no prompt to type into.
    /// </remarks>
    public static bool WantsInteractive(bool oneShot, bool inputRedirected) =>
        !oneShot && !inputRedirected;
}
