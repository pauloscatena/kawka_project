using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Rendering;

/// <summary>
/// Renderer for non-TTY output (pipes, cron, CI): tab-separated, no ANSI, no box drawing,
/// so `cut` and `awk` can consume it. Failures go to stderr so stdout stays parseable.
/// </summary>
/// <remarks>
/// Titles are deliberately dropped. They are decoration, and a pipeline running
/// `kawka topics | cut -f1` should not have to know to skip a line. A command must therefore never
/// put substantive data in a title - if a fact matters, it belongs in a column or a pair.
/// </remarks>
public sealed class PlainTextRenderer(TextWriter output, TextWriter error) : IResultRenderer
{
    public void Render(CommandResult result)
    {
        switch (result)
        {
            case CommandResult.Table t:
                output.WriteLine(string.Join('\t', t.Columns.Select(Escape)));
                foreach (var row in t.Rows) output.WriteLine(string.Join('\t', row.Select(Escape)));
                break;

            case CommandResult.Pairs p:
                foreach (var (k, v) in p.Values) output.WriteLine($"{Escape(k)}\t{Escape(v)}");
                break;

            case CommandResult.Text x:
                output.WriteLine(x.Message);
                break;

            case CommandResult.Failure f:
                error.WriteLine(f.Message);
                break;

            case CommandResult.Group g:
                foreach (var part in g.Parts) Render(part);
                break;
        }
    }

    /// <summary>
    /// Keeps one record on one line and one field between tabs.
    /// </summary>
    /// <remarks>
    /// Message payloads are arbitrary bytes, so a value can legitimately contain a tab or a
    /// newline. Written raw, a tab shifts every later field of that row - `cut -f2` then returns
    /// half a value on one line and a whole one on the next - and a newline splits the record in
    /// two, which any line-oriented reader counts as an extra row. Escaping keeps the output
    /// lossless and machine-readable; the reader can unescape if it cares.
    /// Only applied to tabular output: <c>Text</c> is prose meant for a human to read, and
    /// mangling a multi-line message there would help nobody.
    /// </remarks>
    private static string Escape(string value)
    {
        var escaped = new System.Text.StringBuilder(value.Length);

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': escaped.Append(@"\\"); break;
                case '\t': escaped.Append(@"\t"); break;
                case '\r': escaped.Append(@"\r"); break;
                case '\n': escaped.Append(@"\n"); break;

                // Any other control character, NUL above all. A message body is arbitrary bytes,
                // and a raw NUL truncates the line for anything reading it through a C string -
                // silently, and only for that one record.
                default:
                    if (char.IsControl(c)) escaped.Append(@"\x").Append(((int)c).ToString("x2"));
                    else escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }
}
