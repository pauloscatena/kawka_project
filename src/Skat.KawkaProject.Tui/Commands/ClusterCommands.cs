using System.Globalization;
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class BrokersCommand(IClusterService cluster) : ITuiCommand
{
    public string Name => "brokers";
    public string Usage => "brokers";
    public string Summary => "List cluster brokers";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var rows = (await cluster.ListBrokersAsync(ctx.RequireSession()))
            .OrderBy(b => b.BrokerId)
            .Select(b => (IReadOnlyList<string>)new[]
            {
                b.BrokerId.ToString(CultureInfo.InvariantCulture),
                b.Host,
                b.Port.ToString(CultureInfo.InvariantCulture),
                b.IsController ? "yes" : ""
            })
            .ToList();

        return new CommandResult.Table("Brokers", new[] { "ID", "HOST", "PORT", "CONTROLLER" }, rows);
    }
}

public sealed class GroupsCommand(IClusterService cluster) : ITuiCommand
{
    public string Name => "groups";
    public string Usage => "groups";
    public string Summary => "List consumer groups";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var rows = (await cluster.ListConsumerGroupsAsync(ctx.RequireSession()))
            .OrderBy(g => g.GroupId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (IReadOnlyList<string>)new[]
            {
                g.GroupId, g.State, g.MemberCount.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        return new CommandResult.Table("Consumer groups", new[] { "GROUP", "STATE", "MEMBERS" }, rows);
    }
}

public sealed class LagCommand(IClusterService cluster) : ITuiCommand
{
    public string Name => "lag";
    public string Usage => "lag <group>";
    public string Summary => "Show per-partition lag for a consumer group";
    public bool RequiresSession => true;

    /// <remarks>
    /// The total goes in a Pairs, not in the table title. The total is the number someone runs this
    /// command to see, and the plain-text renderer drops titles by design - in a title it would
    /// vanish exactly when the command is piped into something. Same rule as describe: if a fact
    /// matters, it goes in a column or a pair.
    /// </remarks>
    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing group id. Usage: {Usage}", ExitCodes.Usage);

        var session = ctx.RequireSession();
        var group = ctx.Parsed.Args[0];
        var lags = (await cluster.GetGroupLagAsync(session, group)).ToList();

        // A group that does not exist answers exactly like one that exists with nothing committed:
        // no partitions, no lag. Those mean very different things to someone checking whether a
        // consumer is keeping up, so when the answer is empty - and only then, to keep the normal
        // path at one round trip - ask whether the group is there at all.
        if (lags.Count == 0)
        {
            var known = (await cluster.ListConsumerGroupsAsync(session))
                .Any(g => string.Equals(g.GroupId, group, StringComparison.Ordinal));

            if (!known)
                return new CommandResult.Failure(
                    $"No consumer group '{group}' on this cluster. Run 'groups' to see what is there.",
                    ExitCodes.Usage);
        }

        // Stated even when there are no rows: "no partitions listed" and "fully caught up" look the
        // same otherwise, and they mean different things - no committed offsets versus zero lag.
        var summary = new CommandResult.Pairs($"Lag for '{group}'", new Dictionary<string, string>
        {
            ["group"] = group,
            ["partitions"] = lags.Count.ToString(CultureInfo.InvariantCulture),
            ["total lag"] = lags.Sum(l => l.Lag).ToString(CultureInfo.InvariantCulture)
        });

        // InvariantCulture and no separators: the LAG column is read back by scripts at least as
        // often as by people, and "149.900" parses as 149 in most of them.
        var rows = lags
            // OrdinalIgnoreCase like every other name listing here: Ordinal puts every capitalised
            // topic in a block ahead of the lowercase ones, which reads as an unsorted list.
            .OrderBy(l => l.Topic, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Partition)
            .Select(l => (IReadOnlyList<string>)new[]
            {
                l.Topic,
                l.Partition.ToString(CultureInfo.InvariantCulture),
                l.CurrentOffset.ToString(CultureInfo.InvariantCulture),
                l.EndOffset.ToString(CultureInfo.InvariantCulture),
                l.Lag.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        return new CommandResult.Group(new CommandResult[]
        {
            summary,
            new CommandResult.Table("Partitions", new[] { "TOPIC", "P", "CURRENT", "END", "LAG" }, rows)
        });
    }
}
