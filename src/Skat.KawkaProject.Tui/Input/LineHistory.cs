namespace Skat.KawkaProject.Tui.Input;

/// <summary>
/// The command history behind the up and down arrows, persisted between sessions.
/// </summary>
/// <remarks>
/// Like a shell's history file, this records command lines verbatim - which includes anything typed
/// as an argument. `produce orders --value "..."` puts that payload on disk. That matches what every
/// shell does and is what makes the history useful, but it is worth knowing before typing something
/// sensitive at the prompt.
/// </remarks>
public sealed class LineHistory
{
    /// <summary>Kept bounded so a long-lived history file cannot grow forever.</summary>
    private const int MaxPersistedEntries = 500;

    private readonly List<string> _entries = new();
    private int _cursor;                     // _entries.Count means "past the newest" (empty line)

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) { ResetCursor(); return; }

        // Only consecutive repeats are dropped. Running an earlier command again is normal use;
        // it is holding Enter that produces noise.
        if (_entries.Count > 0 && _entries[^1] == line) { ResetCursor(); return; }

        _entries.Add(line);
        ResetCursor();
    }

    /// <summary>
    /// Puts the cursor back past the newest entry, so the next Up starts from the end rather than
    /// wherever the user last browsed to - which would feel like the history had skipped entries.
    /// </summary>
    public void ResetCursor() => _cursor = _entries.Count;

    public string Previous()
    {
        if (_entries.Count == 0) return "";
        if (_cursor > 0) _cursor--;
        return _entries[_cursor];
    }

    public string Next()
    {
        if (_entries.Count == 0) return "";
        if (_cursor < _entries.Count) _cursor++;
        return _cursor >= _entries.Count ? "" : _entries[_cursor];
    }

    /// <summary>
    /// Reads the history file, or carries on without one.
    /// </summary>
    /// <remarks>
    /// Swallows I/O failures on purpose, which is the opposite of what the profile store should do:
    /// profiles ARE the data, while history is a convenience. Refusing to open the REPL because a
    /// scratch file is unreadable would trade the whole tool for a nice-to-have.
    /// </remarks>
    public void Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            _entries.Clear();
            _entries.AddRange(File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)));
            ResetCursor();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Start with an empty history rather than no session.
        }
    }

    /// <summary>Writes the newest entries, creating the directory if it is missing.</summary>
    public void Save(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(path, _entries.TakeLast(MaxPersistedEntries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same bargain as Load: losing the history is better than failing on the way out.
        }
    }
}
