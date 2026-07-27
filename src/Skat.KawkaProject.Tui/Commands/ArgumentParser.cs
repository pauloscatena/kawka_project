using System.Text;

namespace Skat.KawkaProject.Tui.Commands;

public static class ArgumentParser
{
    public static ParsedCommand ParseLine(string line) => Parse(Tokenize(line));

    /// <summary>
    /// Parses process argv. Nothing here is quoted: the shell already stripped quotes before the
    /// tokens reached us, so <c>--flag=value</c> is the only way to pass a value starting with
    /// dashes in one-shot mode.
    /// </summary>
    public static ParsedCommand ParseArgv(IReadOnlyList<string> tokens) =>
        Parse(tokens.Select(t => new Token(t, Quoted: false)).ToList());

    /// <param name="Quoted">
    /// Whether the token came from a quoted section. This is what makes a value starting with two
    /// dashes expressible at the REPL - <c>--value "--text"</c> - and what lets an explicitly empty
    /// value survive as an empty string instead of vanishing.
    /// </param>
    private readonly record struct Token(string Text, bool Quoted);

    private static ParsedCommand Parse(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count == 0)
            return new ParsedCommand("", Array.Empty<string>(), new Dictionary<string, string?>());

        var verb = tokens[0].Text;
        var args = new List<string>();
        var flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!IsFlag(token)) { args.Add(token.Text); continue; }

            var body = token.Text[2..];

            var equals = body.IndexOf('=');
            if (equals >= 0)
            {
                var namedByEquals = body[..equals];
                // "--=value" names nothing. Treat the whole token as a positional rather than
                // inventing an empty-named flag.
                if (namedByEquals.Length == 0) { args.Add(token.Text); continue; }
                flags[namedByEquals] = body[(equals + 1)..];
                continue;
            }

            // A flag takes the next token as its value unless that token is itself a flag.
            var hasValue = i + 1 < tokens.Count && !IsFlag(tokens[i + 1]);
            flags[body] = hasValue ? tokens[++i].Text : null;
        }

        return new ParsedCommand(verb, args, flags);
    }

    /// <summary>
    /// A quoted token is never a flag - that is the escape for text that starts with dashes. A bare
    /// "--" is not one either: it names nothing, and admitting it would make HasFlag("") answer true
    /// for something nobody asked for.
    /// </summary>
    private static bool IsFlag(Token token) =>
        !token.Quoted
        && token.Text.Length > 2
        && token.Text.StartsWith("--", StringComparison.Ordinal);

    private static List<Token> Tokenize(string line)
    {
        var tokens = new List<Token>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoted = false;   // this token had a quoted section, even an empty one

        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; quoted = true; continue; }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                // `quoted` and not just Length: `--value ""` has to reach the command as an empty
                // string, otherwise it is indistinguishable from never passing --value at all.
                if (current.Length > 0 || quoted)
                {
                    tokens.Add(new Token(current.ToString(), quoted));
                    current.Clear();
                    quoted = false;
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0 || quoted) tokens.Add(new Token(current.ToString(), quoted));
        return tokens;
    }
}
