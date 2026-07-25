namespace Skat.KawkaProject.Tui.Commands;

public static class ArgumentParser
{
    public static ParsedCommand ParseLine(string line) => ParseArgv(Tokenize(line));

    public static ParsedCommand ParseArgv(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return new ParsedCommand("", Array.Empty<string>(), new Dictionary<string, string?>());

        var verb = tokens[0];
        var args = new List<string>();
        var flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { args.Add(token); continue; }

            var name = token[2..];
            // A flag takes the next token as its value unless that token is itself a flag.
            var hasValue = i + 1 < tokens.Count && !tokens[i + 1].StartsWith("--", StringComparison.Ordinal);
            flags[name] = hasValue ? tokens[++i] : null;
        }

        return new ParsedCommand(verb, args, flags);
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
