using Spectre.Console;
using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Rendering;

/// <summary>Renderer for interactive terminals: bordered tables, colour, aligned columns.</summary>
/// <remarks>
/// Every value that came from the cluster goes through <see cref="Markup.Escape"/>. Topic and
/// consumer group names are user-chosen strings that may contain square brackets, which Spectre
/// would otherwise read as markup - rendering them as colour at best, and throwing on an
/// unbalanced tag in the middle of a listing at worst.
/// </remarks>
public sealed class SpectreRenderer(IAnsiConsole console) : IResultRenderer
{
    public void Render(CommandResult result)
    {
        switch (result)
        {
            case CommandResult.Table t:
            {
                var table = new Table().Border(TableBorder.Rounded);
                if (t.Title is not null) table.Title = new TableTitle(t.Title);
                foreach (var c in t.Columns) table.AddColumn(new TableColumn($"[bold]{Markup.Escape(c)}[/]"));
                foreach (var row in t.Rows) table.AddRow(row.Select(cell => Markup.Escape(cell)).ToArray());
                console.Write(table);
                break;
            }

            case CommandResult.Pairs p:
            {
                var grid = new Grid().AddColumn().AddColumn();
                foreach (var (k, v) in p.Values)
                    grid.AddRow($"[dim]{Markup.Escape(k)}[/]", Markup.Escape(v));
                if (p.Title is not null) console.MarkupLine($"[bold]{Markup.Escape(p.Title)}[/]");
                console.Write(grid);
                break;
            }

            case CommandResult.Text x:
                console.MarkupLine(Markup.Escape(x.Message));
                break;

            case CommandResult.Failure f:
                console.MarkupLine($"[red]{Markup.Escape(f.Message)}[/]");
                break;
        }
    }
}
