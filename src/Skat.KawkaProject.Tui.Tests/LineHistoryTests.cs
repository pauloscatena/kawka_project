using Skat.KawkaProject.Tui.Input;

namespace Skat.KawkaProject.Tui.Tests;

public class LineHistoryTests
{
    [Fact]
    public void Previous_walks_backwards_then_stops_at_the_oldest()
    {
        var h = new LineHistory();
        h.Add("topics"); h.Add("describe orders");

        Assert.Equal("describe orders", h.Previous());
        Assert.Equal("topics", h.Previous());
        Assert.Equal("topics", h.Previous());
    }

    [Fact]
    public void Next_walks_forward_and_returns_empty_past_the_newest()
    {
        var h = new LineHistory();
        h.Add("a"); h.Add("b");
        h.Previous(); h.Previous();

        Assert.Equal("b", h.Next());
        Assert.Equal("", h.Next());
    }

    [Fact]
    public void Add_ignores_blanks_and_consecutive_duplicates()
    {
        var h = new LineHistory();
        h.Add("topics"); h.Add("topics"); h.Add("   ");

        Assert.Equal("topics", h.Previous());
        Assert.Equal("topics", h.Previous());   // only one entry exists
    }

    [Fact]
    public void A_repeat_that_is_not_consecutive_is_kept()
    {
        // Re-running an earlier command is normal; only the "pressed Enter twice" case is noise.
        var h = new LineHistory();
        h.Add("topics"); h.Add("brokers"); h.Add("topics");

        Assert.Equal("topics", h.Previous());
        Assert.Equal("brokers", h.Previous());
        Assert.Equal("topics", h.Previous());
    }

    [Fact]
    public void Adding_after_browsing_returns_the_cursor_to_the_end()
    {
        // Otherwise the next Up starts from wherever the user last browsed to, which feels like
        // the history skipped entries.
        var h = new LineHistory();
        h.Add("a"); h.Add("b");
        h.Previous(); h.Previous();          // sitting on "a"

        h.Add("c");

        Assert.Equal("c", h.Previous());
    }

    [Fact]
    public void An_empty_history_is_navigable_without_blowing_up()
    {
        var h = new LineHistory();

        Assert.Equal("", h.Previous());
        Assert.Equal("", h.Next());
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kawka-hist-{Guid.NewGuid():N}");
        try
        {
            var written = new LineHistory();
            written.Add("topics"); written.Add("brokers");
            written.Save(path);

            var read = new LineHistory();
            read.Load(path);

            Assert.Equal("brokers", read.Previous());
            Assert.Equal("topics", read.Previous());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Loading_a_file_that_is_not_there_is_not_an_error()
    {
        var h = new LineHistory();

        h.Load(Path.Combine(Path.GetTempPath(), $"kawka-missing-{Guid.NewGuid():N}"));

        Assert.Equal("", h.Previous());
    }

    [Fact]
    public void An_unreadable_history_file_does_not_stop_the_session_from_starting()
    {
        // History is a convenience. Refusing to open the REPL because a scratch file is a directory,
        // or owned by someone else, would trade a nice-to-have for the whole tool.
        var path = Path.Combine(Path.GetTempPath(), $"kawka-hist-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);        // a directory where a file is expected
        try
        {
            var h = new LineHistory();

            var ex = Record.Exception(() => h.Load(path));

            Assert.Null(ex);
        }
        finally { Directory.Delete(path); }
    }

    [Fact]
    public void Save_creates_the_directory_it_needs()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"kawka-hist-new-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "history");
        try
        {
            var h = new LineHistory();
            h.Add("topics");

            h.Save(path);

            Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_keeps_the_file_from_growing_without_bound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kawka-hist-cap-{Guid.NewGuid():N}");
        try
        {
            var h = new LineHistory();
            for (var i = 0; i < 600; i++) h.Add($"topics {i}");

            h.Save(path);

            var lines = File.ReadAllLines(path);
            Assert.Equal(500, lines.Length);
            Assert.Equal("topics 599", lines[^1]);   // the newest survive, not the oldest
        }
        finally { File.Delete(path); }
    }
}
