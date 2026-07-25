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

    [Fact]
    public void A_quoted_value_starting_with_dashes_is_text_not_a_flag()
    {
        // produce takes free text, and free text can start with anything. Without quotes there is
        // no way to say "this is a value" - the message would silently become two empty flags and
        // the record would be published without its payload.
        var parsed = ArgumentParser.ParseLine("produce orders --value \"--not-a-flag\"");

        Assert.Equal("--not-a-flag", parsed.Flags["value"]);
        Assert.False(parsed.Flags.ContainsKey("not-a-flag"));
    }

    [Fact]
    public void An_equals_sign_binds_a_value_to_its_flag()
    {
        // The escape that survives the shell: quotes are stripped by the shell before argv reaches
        // us, so --value=--not-a-flag is the only way to pass a dashed value in one-shot mode.
        var parsed = ArgumentParser.ParseArgv(new[] { "produce", "orders", "--value=--not-a-flag" });

        Assert.Equal("--not-a-flag", parsed.Flags["value"]);
        Assert.False(parsed.Flags.ContainsKey("not-a-flag"));
    }

    [Fact]
    public void An_explicitly_empty_quoted_value_survives_as_an_empty_string()
    {
        // Publishing an empty payload is a real thing to want. Dropping the token made it
        // indistinguishable from never passing --value at all.
        var parsed = ArgumentParser.ParseLine("produce orders --value \"\" --key k");

        Assert.True(parsed.Flags.ContainsKey("value"));
        Assert.Equal("", parsed.Flags["value"]);
        Assert.Equal("k", parsed.Flags["key"]);
    }

    [Fact]
    public void An_empty_quoted_positional_argument_keeps_its_place()
    {
        // Commands read Args by index. A dropped empty token shifts every argument after it.
        var parsed = ArgumentParser.ParseLine("produce \"\" second");

        Assert.Equal(new[] { "", "second" }, parsed.Args);
    }

    [Fact]
    public void A_bare_double_dash_is_not_a_flag_with_an_empty_name()
    {
        // "--" has no name. Letting it into Flags means HasFlag("") answers true for something
        // nobody asked for, and pollutes the key set that help and diagnostics read.
        var parsed = ArgumentParser.ParseLine("topics --");

        Assert.False(parsed.Flags.ContainsKey(""));
        Assert.Empty(parsed.Flags);
    }

    [Fact]
    public void A_flag_with_no_name_before_the_equals_is_not_a_flag()
    {
        var parsed = ArgumentParser.ParseLine("topics --=value");

        Assert.False(parsed.Flags.ContainsKey(""));
        Assert.Empty(parsed.Flags);
    }
}
