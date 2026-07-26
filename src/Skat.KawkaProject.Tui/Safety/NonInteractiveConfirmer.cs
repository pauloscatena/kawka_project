using Spectre.Console;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Tui.Safety;

/// <summary>
/// Used in one-shot mode and whenever there is no TTY. Refuses by default: with no human to type
/// the topic name, the safe answer is no. A script must state its intent explicitly, which is why
/// the flag is deliberately long and ugly.
/// </summary>
public sealed class NonInteractiveConfirmer(bool acknowledged, IAnsiConsole console) : IConfirmer
{
    /// <summary>
    /// Long on purpose. As --force or -y, muscle memory from other tools would delete a production
    /// topic; nobody types this one without having decided to.
    /// </summary>
    public const string AcknowledgeFlag = "yes-i-know-this-deletes-data";

    public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct)
    {
        // Not even with the flag. Acknowledging "this deletes data" is not the same as naming which
        // data, and a blank name means something upstream lost the topic it was working on.
        if (string.IsNullOrWhiteSpace(action.TopicName))
        {
            console.MarkupLine(
                $"[red]Refusing to {Markup.Escape(action.Verb)} a topic with a blank name.[/]");
            return Task.FromResult(false);
        }

        // Only on refusal. Printing the warning when the job was told to proceed would put a
        // frightening paragraph into the logs of something working exactly as intended.
        if (!acknowledged)
        {
            console.MarkupLine(
                $"[red]Refusing to {Markup.Escape(action.Verb)} '{Markup.Escape(action.TopicName)}' " +
                $"without confirmation.[/] Re-run with [bold]--{AcknowledgeFlag}[/] if you are sure. " +
                $"This would permanently lose: {Markup.Escape(string.Join(", ", action.WhatIsLost))}.");
        }

        return Task.FromResult(acknowledged);
    }
}
