namespace Skat.KawkaProject.Tui;

public static class ExitCodes
{
    public const int Success = 0;
    public const int OperationalFailure = 1;
    public const int Usage = 2;
    public const int ConfirmationRefused = 3;

    /// <summary>
    /// The operator stopped it, so it is not an operational failure. 128 + SIGINT, the shell
    /// convention, so a script retrying on exit code 1 does not retry something a human
    /// deliberately interrupted.
    /// </summary>
    public const int Cancelled = 130;
}
