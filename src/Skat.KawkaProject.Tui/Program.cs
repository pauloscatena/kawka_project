using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Skat.KawkaProject.Tui;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Input;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

var parsed = ArgumentParser.ParseArgv(args);
var oneShot = !string.IsNullOrWhiteSpace(parsed.Verb);

// TTY detection happens ONCE, here, so no code downstream needs to branch on it.
var wantsPlain = parsed.Flag("output") == "tsv"
                 || (!AnsiConsole.Profile.Capabilities.Interactive && parsed.Flag("output") != "text");

var services = new ServiceCollection();
services.AddSingleton<IConnectionProfileRepository, ConnectionProfileRepository>();
services.AddSingleton<IKafkaConnectionFactory, KafkaConnectionFactory>();
services.AddTransient<ITopicService, TopicService>();
services.AddTransient<IMessageService, MessageService>();
services.AddTransient<IClusterService, ClusterService>();

services.AddSingleton<ITuiCommand, ProfilesCommand>();
services.AddSingleton<ITuiCommand, ConnectCommand>();
services.AddSingleton<ITuiCommand, DisconnectCommand>();
services.AddSingleton<ITuiCommand, StatusCommand>();
services.AddSingleton<ITuiCommand, TopicsCommand>();
services.AddSingleton<ITuiCommand, DescribeCommand>();
services.AddSingleton<ITuiCommand, BrokersCommand>();
services.AddSingleton<ITuiCommand, GroupsCommand>();
services.AddSingleton<ITuiCommand, LagCommand>();

// Lazily resolved: help is a command that needs the registry, and the registry needs every
// command. Deferring the lookup breaks the cycle without leaving help out of its own listing.
services.AddSingleton<ITuiCommand>(sp => new HelpCommand(() => sp.GetRequiredService<CommandRegistry>()));

services.AddSingleton(sp => new CommandRegistry(sp.GetServices<ITuiCommand>()));
services.AddSingleton<CommandDispatcher>();

services.AddSingleton<IResultRenderer>(_ => wantsPlain
    ? new PlainTextRenderer(Console.Out, Console.Error)
    : new SpectreRenderer(AnsiConsole.Console));

var historyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kawka", "history");

services.AddSingleton<IKeySource, ConsoleKeySource>();
services.AddSingleton(_ =>
{
    var h = new LineHistory();
    if (!h.Load(historyPath))
        Console.Error.WriteLine($"kawka: could not read {historyPath}; starting with an empty history.");
    return h;
});

// Keyed on IsInputRedirected, not on the renderer's TTY check: they answer different questions.
// `--output text` with piped input keeps the fancy renderer, and the key-reading prompt would then
// throw on the first keystroke - Console.ReadKey refuses when stdin is not a terminal. Reading
// whole lines is the right behaviour without a terminal anyway.
services.AddSingleton<ILineReader>(sp => Console.IsInputRedirected
    ? new ConsoleLineReader()
    : new PromptReader(
        sp.GetRequiredService<IKeySource>(),
        sp.GetRequiredService<LineHistory>(),
        AnsiConsole.Console));

// Phase 4 replaces this with the real confirmers.
services.AddSingleton<IConfirmer>(_ => new NotYetImplementedConfirmer());

services.AddSingleton<TuiHost>();

using var provider = services.BuildServiceProvider();
using var host = provider.GetRequiredService<TuiHost>();

using var cts = new CancellationTokenSource();

// Ctrl+C during a command cancels just that command and returns the prompt; Ctrl+C at an idle
// prompt exits. Matches the Claude Code behaviour the layout is modelled on.
//
// The idle branch deliberately does NOT set e.Cancel. Setting it tells the runtime "I handled
// this, keep running", and the only thing the handler could do instead was signal a token - which
// Console.ReadLine does not observe, because it is blocked in a native read. The process stayed
// alive at the prompt, ignoring Ctrl+C entirely. Leaving e.Cancel false lets the runtime terminate,
// which is what the user asked for; the session is disposed here first, since termination will not
// run the using blocks below.
Console.CancelKeyPress += (_, e) =>
{
    if (host.CancelRunningCommand())
    {
        e.Cancel = true;
        return;
    }

    host.Dispose();
};

if (oneShot)
{
    // One-shot needs its session established up front, since there is no REPL to 'connect' in.
    // Quietly: the user asked for `topics`, not for a "Connected to ..." line ahead of it in
    // whatever is reading stdout. A failure to connect is still reported, and stops the run -
    // continuing would report "no active connection" for a reason we already know.
    var profile = parsed.Flag("profile");
    if (profile is not null)
    {
        var connectCode = await host.RunQuietlyAsync(
            ArgumentParser.ParseLine($"connect {profile}"), cts.Token);
        if (connectCode != ExitCodes.Success) return connectCode;
    }

    return await host.RunOnceAsync(parsed, cts.Token);
}

var replExit = await host.RunReplAsync(cts.Token);

if (!provider.GetRequiredService<LineHistory>().Save(historyPath))
    Console.Error.WriteLine($"kawka: could not write {historyPath}; this session's history is lost.");

return replExit;

file sealed class NotYetImplementedConfirmer : IConfirmer
{
    public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct) =>
        throw new NotSupportedException("Destructive commands are not available yet (Phase 4).");
}
