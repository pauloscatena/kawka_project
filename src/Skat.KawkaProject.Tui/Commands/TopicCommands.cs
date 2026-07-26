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
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (IReadOnlyList<string>)new[]
            {
                t.Name, t.PartitionCount.ToString(), t.ReplicationFactor.ToString()
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
        var overrides = await topics.GetTopicConfigOverridesAsync(session, topicName);

        // Insertion order is what the user reads: identity first, then shape, then configuration.
        var facts = new Dictionary<string, string>
        {
            ["topic"] = detail.Topic.Name,
            ["partitions"] = detail.Partitions.Count.ToString(),
            ["replication factor"] = detail.Topic.ReplicationFactor.ToString()
        };

        if (overrides.Count == 0)
        {
            // Said out loud: an empty set means "no overrides", not "nobody looked". Silence reads
            // as the second, and the difference matters before a destructive operation.
            facts["config"] = "no config overrides";
        }
        else
        {
            foreach (var (key, value) in overrides.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                facts[key] = value;
        }

        var rows = detail.Partitions
            .OrderBy(p => p.PartitionId)
            .Select(p => (IReadOnlyList<string>)new[]
            {
                p.PartitionId.ToString(), p.LeaderBrokerId.ToString(),
                p.EarliestOffset.ToString("N0"), p.LatestOffset.ToString("N0"),
                (p.LatestOffset - p.EarliestOffset).ToString("N0")
            })
            .ToList();

        return new CommandResult.Group(new CommandResult[]
        {
            new CommandResult.Pairs(detail.Topic.Name, facts),
            new CommandResult.Table("Partitions", new[] { "P", "LEADER", "EARLIEST", "LATEST", "COUNT" }, rows)
        });
    }
}
