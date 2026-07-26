using Spectre.Console;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Tui.Safety;

/// <summary>
/// Mirrors the GUI's type-the-name gate. One attempt: a mismatch aborts the command rather than
/// re-prompting, so a mistyped name never turns into "try again until it works".
/// </summary>
public sealed class InteractiveConfirmer(IAnsiConsole console, Func<string?> readLine) : IConfirmer
{
    public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct)
    {
        var lines = new List<string>
        {
            $"[bold red]This will {Markup.Escape(action.Verb)} '{Markup.Escape(action.TopicName)}'. It cannot be undone.[/]",
            "",
            "[red]Permanently lost:[/]"
        };
        lines.AddRange(action.WhatIsLost.Select(w => $"  • {Markup.Escape(w)}"));

        // Not decoration: a prompt listing only the losses sends the operator off to re-apply
        // configuration the operation already carried over. The TUI has no headline of its own, so
        // it shows the canonical lists whole - both halves.
        if (action.WhatIsPreserved.Count > 0)
        {
            lines.Add("");
            lines.Add("[green]Preserved:[/]");
            lines.AddRange(action.WhatIsPreserved.Select(w => $"  • {Markup.Escape(w)}"));
        }

        console.Write(new Panel(new Markup(string.Join('\n', lines)))
        {
            Border = BoxBorder.Heavy,
            BorderStyle = new Style(Color.Red),
            Header = new PanelHeader(" DESTRUCTIVE ")
        });

        console.Markup($"Type [bold]{Markup.Escape(action.TopicName)}[/] to confirm: ");
        var typed = readLine();

        // Ordinal and exact: no trimming, no case folding, no accepting "y". Every one of those
        // conveniences is a way to destroy a topic the operator did not mean to name.
        var ok = string.Equals(typed, action.TopicName, StringComparison.Ordinal);
        if (!ok) console.MarkupLine("[yellow]Name did not match — aborted.[/]");
        return Task.FromResult(ok);
    }
}
