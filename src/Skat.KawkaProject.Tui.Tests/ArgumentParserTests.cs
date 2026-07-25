using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Tests;

public class ArgumentParserTests
{
    [Fact]
    public void ParseLine_splits_verb_args_and_flags()
    {
        var parsed = ArgumentParser.ParseLine("describe orders --output tsv --no-color");

        Assert.Equal("describe", parsed.Verb);
        Assert.Equal(new[] { "orders" }, parsed.Args);
        Assert.Equal("tsv", parsed.Flags["output"]);
        Assert.True(parsed.Flags.ContainsKey("no-color"));
        Assert.Null(parsed.Flags["no-color"]);
    }

    [Fact]
    public void ParseLine_keeps_quoted_values_together()
    {
        var parsed = ArgumentParser.ParseLine("produce orders --value \"hello world\"");

        Assert.Equal("hello world", parsed.Flags["value"]);
    }

    [Fact]
    public void ParseLine_returns_empty_verb_for_blank_input()
    {
        Assert.Equal("", ArgumentParser.ParseLine("   ").Verb);
    }

    [Fact]
    public void ParseArgv_behaves_like_ParseLine()
    {
        var parsed = ArgumentParser.ParseArgv(new[] { "topics", "--profile", "prod" });

        Assert.Equal("topics", parsed.Verb);
        Assert.Equal("prod", parsed.Flags["profile"]);
    }
}
