namespace Skat.KawkaProject.Core.Models;

/// <summary>
/// Describes a destructive topic operation and what it irreversibly loses. Presentation-agnostic:
/// the GUI renders these lists in a warning panel and the (planned) TUI in a confirmation prompt -
/// one canonical list instead of a copy per frontend.
/// HOW the operation is confirmed (modal vs type-the-name) is a per-frontend concern and is NOT
/// modelled here.
/// </summary>
public sealed record DestructiveAction(
    string TopicName,
    string Verb,
    IReadOnlyList<string> WhatIsLost,
    IReadOnlyList<string> WhatIsPreserved)
{
    /// <summary>What a shrink-by-recreate destroys. ACLs are deliberately excluded: literal ACLs on
    /// the same topic name survive delete+recreate, so claiming they are lost would be wrong.</summary>
    public static IReadOnlyList<string> RecreateLoses { get; } = new[]
    {
        "all messages in the topic",
        "committed consumer group offsets (consumers may then silently skip or replay records)"
    };

    /// <summary>What survives it. Kept beside the losses because a warning that omits this sends the
    /// user to re-apply settings the operation already carried over.</summary>
    public static IReadOnlyList<string> RecreatePreserves { get; } = new[]
    {
        "topic-level config overrides"
    };

    /// <summary>The recreate of a specific topic. Callers that only need the wording (a static
    /// warning panel) can read the lists directly instead of naming a topic.</summary>
    public static DestructiveAction Recreate(string topicName) =>
        new(topicName, "recreate", RecreateLoses, RecreatePreserves);
}
