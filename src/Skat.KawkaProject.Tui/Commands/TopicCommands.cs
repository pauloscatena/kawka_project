using System.Globalization;
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class TopicsCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "topics";
    public string Usage => "topics [filter]";
    public string Summary => "List topics, optionally filtered by substring";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var all = await topics.ListTopicsAsync(ctx.RequireSession());
        var filter = ctx.Parsed.Args.Count > 0 ? ctx.Parsed.Args[0] : null;

        var rows = all
            .Where(t => filter is null || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            // Ordered the same way it is filtered. Ordinal would sort every capitalised name into a
            // block ahead of the lowercase ones, which reads as "not sorted" to someone scanning.
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => (IReadOnlyList<string>)new[]
            {
                t.Name,
                t.PartitionCount.ToString(CultureInfo.InvariantCulture),
                t.ReplicationFactor.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        return new CommandResult.Table(
            filter is null ? "Topics" : $"Topics matching '{filter}'",
            new[] { "NAME", "PARTS", "RF" }, rows);
    }
}

public sealed class DescribeCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "describe";
    public string Usage => "describe <topic>";
    public string Summary => "Show partitions, offsets and config overrides for a topic";
    public bool RequiresSession => true;

    /// <remarks>
    /// Returns a Group rather than folding the topic-level facts into a table title. Titles are
    /// decoration and the plain-text renderer drops them, so anything written there disappears the
    /// moment the command is piped - and config overrides are exactly what an operator checks
    /// before a destructive operation. If a fact matters, it goes in a column or a pair.
    /// </remarks>
    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var session = ctx.RequireSession();
        var topicName = ctx.Parsed.Args[0];
        var detail = await topics.GetTopicDetailAsync(session, topicName);

        var parts = new List<CommandResult>
        {
            // Identity in its own Pairs, kept apart from the overrides. Merging them into one
            // dictionary meant a config named "topic" would overwrite the topic's own name - on the
            // screen someone reads right before deleting it. No Kafka config is called that today;
            // the separation costs nothing and removes the question.
            new CommandResult.Pairs(detail.Topic.Name, new Dictionary<string, string>
            {
                ["topic"] = detail.Topic.Name,
                ["partitions"] = detail.Partitions.Count.ToString(CultureInfo.InvariantCulture),
                ["replication factor"] = detail.Topic.ReplicationFactor.ToString(CultureInfo.InvariantCulture)
            })
        };

        parts.Add(await ReadConfigAsync(session, topicName));

        // InvariantCulture, and no thousands separators: this output is parsed by cut and awk as
        // often as it is read. Under pt-BR, "N0" renders 1204 as "1.204", which a script cannot
        // read back as a number.
        var rows = detail.Partitions
            .OrderBy(p => p.PartitionId)
            .Select(p => (IReadOnlyList<string>)new[]
            {
                p.PartitionId.ToString(CultureInfo.InvariantCulture),
                p.LeaderBrokerId.ToString(CultureInfo.InvariantCulture),
                p.EarliestOffset.ToString(CultureInfo.InvariantCulture),
                p.LatestOffset.ToString(CultureInfo.InvariantCulture),
                (p.LatestOffset - p.EarliestOffset).ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        parts.Add(new CommandResult.Table("Partitions",
            new[] { "P", "LEADER", "EARLIEST", "LATEST", "COUNT" }, rows));

        return new CommandResult.Group(parts);
    }

    /// <summary>
    /// The config overrides, or a failure describing why they could not be read.
    /// </summary>
    /// <remarks>
    /// The one place in this command that catches. The dispatcher is the general boundary, but
    /// letting this call's failure propagate would discard the partitions already fetched by the
    /// previous one - the operator sees nothing at all, having already paid for the data that did
    /// arrive. Returning the failure as a part keeps it visible and keeps the exit code non-zero.
    /// </remarks>
    private async Task<CommandResult> ReadConfigAsync(IKafkaSession session, string topicName)
    {
        try
        {
            var overrides = await topics.GetTopicConfigOverridesAsync(session, topicName);

            // Said out loud: an empty set means "no overrides", not "nobody looked". Silence reads
            // as the second, and the difference matters before a destructive operation.
            if (overrides.Count == 0)
                return new CommandResult.Pairs("Config", new Dictionary<string, string>
                {
                    ["config"] = "no config overrides"
                });

            var ordered = new Dictionary<string, string>();
            foreach (var (key, value) in overrides.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                ordered[key] = value;

            return new CommandResult.Pairs("Config overrides", ordered);
        }
        catch (Exception ex)
        {
            return new CommandResult.Failure(
                $"Could not read config overrides for '{topicName}': {ex.Message}",
                ExitCodes.OperationalFailure);
        }
    }
}
