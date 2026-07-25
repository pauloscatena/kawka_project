namespace Skat.KawkaProject.Tui;

/// <summary>
/// Placeholder entry point, replaced in Task 6 by the real one (argv dispatch, REPL and DI).
/// </summary>
/// <remarks>
/// It exists from Task 1 because a console project without a Main does not compile (CS5001), and
/// the plan does not create the real entry point until Task 6 - which would leave Tasks 1 to 5
/// unable to build or run a single test. It returns OperationalFailure rather than Success so that
/// anyone running the half-assembled tool gets a non-zero exit instead of silent success.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args) => ExitCodes.OperationalFailure;
}
