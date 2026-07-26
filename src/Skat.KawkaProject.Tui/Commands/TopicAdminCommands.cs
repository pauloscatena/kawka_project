using System.Globalization;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Tui.Commands;

/// <summary>
/// The topic name a destructive command was given, or a failure explaining why there is none.
/// </summary>
/// <remarks>
/// A bare Args.Count check is not enough: the parser preserves a quoted empty token, so
/// `delete ""` arrives with one argument that happens to be blank. The confirmers refuse a blank
/// name as well, but a destructive command should never get that far - and the message here can
/// say what is actually wrong.
/// </remarks>
internal static class TopicArgument
{
    public static bool TryRead(CommandContext ctx, string usage, out string topicName, out CommandResult? failure)
    {
        topicName = ctx.Parsed.Args.Count > 0 ? ctx.Parsed.Args[0] : "";

        if (string.IsNullOrWhiteSpace(topicName))
        {
            failure = new CommandResult.Failure($"Missing topic name. Usage: {usage}", ExitCodes.Usage);
            return false;
        }

        failure = null;
        return true;
    }
}

public sealed class CreateCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "create";
    public string Usage => "create <topic> --partitions N [--replication N]";
    public string Summary => "Create a topic";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (!TopicArgument.TryRead(ctx, Usage, out var topicName, out var failure)) return failure!;

        var partitions = ctx.Parsed.IntFlag("partitions");
        if (partitions is null)
            return new CommandResult.Failure($"Missing --partitions. Usage: {Usage}", ExitCodes.Usage);

        var replication = (short)(ctx.Parsed.IntFlag("replication") ?? 1);

        await topics.CreateTopicAsync(ctx.RequireSession(), topicName, partitions.Value, replication);
        return new CommandResult.Text(
            $"Created '{topicName}' with {partitions.Value.ToString(CultureInfo.InvariantCulture)} partitions, "
            + $"RF {replication.ToString(CultureInfo.InvariantCulture)}.");
    }
}

public sealed class DeleteCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "delete";
    public string Usage => "delete <topic>";
    public string Summary => "Delete a topic (destructive)";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (!TopicArgument.TryRead(ctx, Usage, out var topicName, out var failure)) return failure!;

        // The canonical list from Core, not a local copy - the same home the GUI's warning reads
        // from. Asking happens before anything is destroyed; confirming afterwards would be theatre.
        if (!await ctx.Confirmer.ConfirmAsync(DestructiveAction.Delete(topicName), ct))
            return new CommandResult.Failure($"Aborted: '{topicName}' was not deleted.", ExitCodes.ConfirmationRefused);

        await topics.DeleteTopicAsync(ctx.RequireSession(), topicName);
        return new CommandResult.Text($"Deleted '{topicName}'.");
    }
}

public sealed class IncreaseCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "increase";
    public string Usage => "increase <topic> --to N";
    public string Summary => "Increase a topic's partition count (non-destructive)";
    public bool RequiresSession => true;

    /// <remarks>
    /// No confirmation: growing a partition count destroys nothing. Prompting here would train the
    /// operator to type topic names without reading, for the times it does matter.
    /// </remarks>
    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (!TopicArgument.TryRead(ctx, Usage, out var topicName, out var failure)) return failure!;

        var target = ctx.Parsed.IntFlag("to");
        if (target is null)
            return new CommandResult.Failure($"Missing --to. Usage: {Usage}", ExitCodes.Usage);

        await topics.ExpandPartitionsAsync(ctx.RequireSession(), topicName, target.Value);
        return new CommandResult.Text(
            $"'{topicName}' now has {target.Value.ToString(CultureInfo.InvariantCulture)} partitions.");
    }
}

public sealed class RecreateCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "recreate";
    public string Usage => "recreate <topic> --to N";
    public string Summary => "Delete and recreate a topic with fewer partitions (destructive)";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (!TopicArgument.TryRead(ctx, Usage, out var topicName, out var failure)) return failure!;

        var target = ctx.Parsed.IntFlag("to");
        if (target is null)
            return new CommandResult.Failure($"Missing --to. Usage: {Usage}", ExitCodes.Usage);

        var session = ctx.RequireSession();

        // Checked before the confirmation, not after. The service refuses an impossible target
        // anyway, so nothing was ever at risk - but making someone type a topic name to authorise
        // an operation that could never run is how you teach them to type it without reading.
        var current = (await topics.GetTopicDetailAsync(session, topicName)).Partitions.Count;
        if (target.Value >= current)
            return new CommandResult.Failure(
                $"'{topicName}' has {current.ToString(CultureInfo.InvariantCulture)} partitions, and recreate only "
                + $"reduces them. Use --to between 1 and {(current - 1).ToString(CultureInfo.InvariantCulture)}, "
                + "or 'increase' to add partitions.", ExitCodes.Usage);

        // Core's canonical list, the same one the GUI's warning panel reads.
        if (!await ctx.Confirmer.ConfirmAsync(DestructiveAction.Recreate(topicName), ct))
            return new CommandResult.Failure($"Aborted: '{topicName}' was not modified.", ExitCodes.ConfirmationRefused);

        try
        {
            // No replication factor is passed: the service derives it from the live topic, so a
            // reassignment completing between here and the recreate cannot silently rebuild the
            // topic with a stale factor.
            await topics.DeleteAndRecreateTopicAsync(session, topicName, target.Value);
            return new CommandResult.Text(
                $"'{topicName}' recreated with {target.Value.ToString(CultureInfo.InvariantCulture)} partitions.");
        }
        catch (TopicRecreateFailedException ex) when (ex.TopicMayBeDeleted)
        {
            // Deletion is asynchronous and irrevocable once issued, so any failure at or after that
            // point is potential data loss. The preserved config goes into the message because the
            // scrollback is the only record the user has left of how the topic was configured.
            var config = ex.PreservedConfig.Count > 0
                ? string.Join(", ", ex.PreservedConfig.Select(kv => $"{kv.Key}={kv.Value}"))
                : "(none)";

            return new CommandResult.Failure(
                $"DATA LOSS RISK: deletion of '{topicName}' was already issued and cannot be undone, but the " +
                $"topic could not be recreated: {ex.InnerException?.Message ?? ex.Message}. " +
                $"Verify it on your cluster and recreate manually if needed. " +
                $"It had {ex.Attempt.OriginalPartitionCount.ToString(CultureInfo.InvariantCulture)} partitions, " +
                $"replication factor {ex.Attempt.ReplicationFactor.ToString(CultureInfo.InvariantCulture)}, " +
                $"config overrides: {config}",
                ExitCodes.OperationalFailure);
        }
        catch (TopicRecreateFailedException ex)
        {
            // The broker refused the delete - an ACL denial, or delete.topic.enable=false - so the
            // topic is provably intact. Crying data loss here teaches the operator to dismiss the
            // warning that matters.
            return new CommandResult.Failure(
                $"Could not recreate '{topicName}': {ex.InnerException?.Message ?? ex.Message}. " +
                "The topic was NOT modified.", ExitCodes.OperationalFailure);
        }
    }
}
