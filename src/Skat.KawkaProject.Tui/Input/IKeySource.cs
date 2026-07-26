namespace Skat.KawkaProject.Tui.Input;

/// <summary>
/// Abstracts keyboard reading so PromptReader can be tested with a scripted key sequence
/// instead of a real console.
/// </summary>
public interface IKeySource
{
    ConsoleKeyInfo ReadKey();
}

public sealed class ConsoleKeySource : IKeySource
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);
}
