namespace Skat.KawkaProject.Core.Models;

/// <summary>
/// Describes a destructive topic operation and what it irreversibly loses. Presentation-agnostic:
/// the GUI renders these lists in a warning panel and the (planned) TUI in a confirmation prompt -
/// one canonical list instead of a copy per frontend.
/// HOW the operation is confirmed (modal vs type-the-name) is a per-frontend concern and is NOT
/// modelled here.
/// </summary>
/// <remarks>
/// Compare instances by TopicName and Verb, not with ==. The generated record equality compares the
/// two list members by reference, so two actions with identical contents built from separately
/// allocated lists are NOT equal - a test asserting equality against an inline-built expectation
/// would fail for a reason that has nothing to do with what it is testing.
/// </remarks>
public sealed record DestructiveAction(
    string TopicName,
    string Verb,
    IReadOnlyList<string> WhatIsLost,
    IReadOnlyList<string> WhatIsPreserved)
{
    /// <summary>The message loss, named so a frontend that already states it in a headline can drop
    /// it from its inventory line without depending on this list's order. Public because that is a
    /// real caller's need; the other entries stay private until one of them has the same.</summary>
    public const string LostMessages = "all messages in the topic";

    /// <summary>Names auto.offset.reset on purpose: it is the setting that decides whether a
    /// consumer skips or replays, so it is what the user has to go and check before recreating.
    /// "Consumers may skip or replay" alone says something will happen but not where to look.</summary>
    private const string LostOffsets =
        "committed consumer group offsets (depending on auto.offset.reset, consumers may then silently skip or replay records)";

    /// <summary>What a shrink-by-recreate destroys. ACLs are deliberately excluded: literal ACLs on
    /// the same topic name survive delete+recreate, so claiming they are lost would be wrong.</summary>
    // AsReadOnly, not the bare array: IReadOnlyList over string[] is castable back to string[], and
    // these are process-wide statics behind every destructive warning the app shows. One stray cast
    // writing to index 0 would rewrite that warning for every frontend until restart.
    public static IReadOnlyList<string> RecreateLoses { get; } =
        Array.AsReadOnly(new[] { LostMessages, LostOffsets });

    /// <summary>What survives it. Kept beside the losses because a warning that omits this sends the
    /// user to re-apply settings the operation already carried over.</summary>
    public static IReadOnlyList<string> RecreatePreserves { get; } =
        Array.AsReadOnly(new[] { "topic-level config overrides" });

    /// <summary>The recreate of a specific topic. Callers that only need the wording (a static
    /// warning panel) can read the lists directly instead of naming a topic.</summary>
    public static DestructiveAction Recreate(string topicName) =>
        new(topicName, "recreate", RecreateLoses, RecreatePreserves);

    /// <summary>What deleting a topic destroys. Nothing survives, because the topic does not.</summary>
    /// <remarks>
    /// Deliberately not the recreate list. A recreate puts the topic back, so "consumers may skip
    /// or replay" describes what happens next; a delete leaves nothing to consume, and offsets for
    /// a topic that no longer exists are simply gone. ACLs are excluded for the same reason as in
    /// Recreate: literal ACLs on the name survive, so listing them would send someone to re-grant
    /// permissions that were never revoked.
    /// </remarks>
    public static IReadOnlyList<string> DeleteLoses { get; } = Array.AsReadOnly(new[]
    {
        LostMessages,
        "committed consumer group offsets for the topic"
    });

    /// <summary>The delete of a specific topic.</summary>
    public static DestructiveAction Delete(string topicName) =>
        new(topicName, "delete", DeleteLoses, Array.Empty<string>());
}
