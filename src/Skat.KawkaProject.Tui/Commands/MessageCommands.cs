using System.Globalization;
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class ConsumeCommand(IMessageService messages, ITopicService topics) : ITuiCommand
{
    public string Name => "consume";
    public string Usage => "consume <topic> [--partition N] [--from earliest|latest|<offset>] [--limit N]";
    public string Summary => "Read messages from one partition";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var session = ctx.RequireSession();
        var topicName = ctx.Parsed.Args[0];
        var partition = ctx.Parsed.IntFlag("partition") ?? 0;
        var limit = ctx.Parsed.IntFlag("limit") ?? 20;
        if (limit < 1) return new CommandResult.Failure("--limit must be at least 1.", ExitCodes.Usage);

        var startOffset = await ResolveStartOffsetAsync(session, topicName, partition, limit, ctx.Parsed.Flag("from"));

        var fetched = await messages.FetchMessagesAsync(session, topicName, partition, startOffset, limit);

        // InvariantCulture throughout. An offset is an identifier, not a quantity - "1.234.567" is
        // not something anyone can hand back to --from - and a fixed calendar keeps the timestamp
        // readable on a server whose culture uses a different era.
        var rows = fetched
            .Select(m => (IReadOnlyList<string>)new[]
            {
                m.Offset.ToString(CultureInfo.InvariantCulture),
                m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                m.Key ?? "",
                m.Value ?? ""
            })
            .ToList();

        return new CommandResult.Table(
            $"{topicName}[{partition}] from offset {startOffset.ToString(CultureInfo.InvariantCulture)}",
            new[] { "OFFSET", "TIMESTAMP", "KEY", "VALUE" }, rows);
    }

    /// <summary>Maps --from to a concrete offset. 'latest' means "the last &lt;limit&gt; messages",
    /// which is what someone tailing a topic actually wants.</summary>
    private async Task<long> ResolveStartOffsetAsync(
        IKafkaSession session, string topicName, int partition, int limit, string? from)
    {
        if (from is null or "earliest" or "latest")
        {
            var detail = await topics.GetTopicDetailAsync(session, topicName);
            var info = detail.Partitions.FirstOrDefault(p => p.PartitionId == partition)
                ?? throw new ArgumentOutOfRangeException(nameof(partition), partition,
                    $"Topic '{topicName}' has no partition {partition}; it has "
                    + $"{detail.Partitions.Count} (0-{detail.Partitions.Count - 1}).");

            // Clamped to the earliest offset: a topic holding fewer messages than the limit would
            // otherwise be asked for a negative offset.
            return from == "latest" ? Math.Max(info.EarliestOffset, info.LatestOffset - limit) : info.EarliestOffset;
        }

        if (!long.TryParse(from, NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitOffset))
            throw new FormatException($"--from expects 'earliest', 'latest' or a number, got '{from}'.");

        return explicitOffset;
    }
}

public sealed class ProduceCommand(IMessageService messages) : ITuiCommand
{
    public string Name => "produce";
    public string Usage => "produce <topic> [--key K] --value V";
    public string Summary => "Publish a message to a topic";
    public bool RequiresSession => true;

    /// <remarks>
    /// No --partition flag: IMessageService.ProduceAsync does not take one, because the partition
    /// is the partitioner's decision. Adding the flag here would mean inventing a parameter the
    /// service cannot honour.
    /// </remarks>
    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        // Null, not empty: `--value ""` publishes an empty body, which is a different thing from
        // forgetting the flag.
        var value = ctx.Parsed.Flag("value");
        if (value is null)
            return new CommandResult.Failure($"Missing --value. Usage: {Usage}", ExitCodes.Usage);

        var topicName = ctx.Parsed.Args[0];
        await messages.ProduceAsync(ctx.RequireSession(), topicName, ctx.Parsed.Flag("key"), value);

        return new CommandResult.Text($"Produced 1 message to '{topicName}'.");
    }
}
