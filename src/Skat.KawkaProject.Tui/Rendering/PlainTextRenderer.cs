using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Rendering;

/// <summary>
/// Renderer for non-TTY output (pipes, cron, CI): tab-separated, no ANSI, no box drawing,
/// so `cut` and `awk` can consume it. Failures go to stderr so stdout stays parseable.
/// </summary>
/// <remarks>
/// Titles are deliberately dropped. They are decoration, and a pipeline running
/// `kawka topics | cut -f1` should not have to know to skip a line.
/// </remarks>
public sealed class PlainTextRenderer(TextWriter output, TextWriter error) : IResultRenderer
{
    public void Render(CommandResult result)
    {
        switch (result)
        {
            case CommandResult.Table t:
                output.WriteLine(string.Join('\t', t.Columns));
                foreach (var row in t.Rows) output.WriteLine(string.Join('\t', row));
                break;

            case CommandResult.Pairs p:
                foreach (var (k, v) in p.Values) output.WriteLine($"{k}\t{v}");
                break;

            case CommandResult.Text x:
                output.WriteLine(x.Message);
                break;

            case CommandResult.Failure f:
                error.WriteLine(f.Message);
                break;
        }
    }
}
