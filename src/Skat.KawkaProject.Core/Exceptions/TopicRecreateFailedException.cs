namespace Skat.KawkaProject.Core.Exceptions;

/// <summary>Which step of the delete-and-recreate sequence failed.</summary>
public enum TopicRecreateStage
{
    /// <summary>Reading the topic's config overrides. Nothing destructive has happened yet.</summary>
    ReadingConfig,

    /// <summary>Issuing the delete.</summary>
    Deleting,

    /// <summary>Waiting for the deletion to propagate. The delete WAS accepted by the controller.</summary>
    WaitingForDeletion,

    /// <summary>Recreating the topic. The old topic and all its messages are gone.</summary>
    Creating
}

/// <summary>
/// Everything needed to recreate the topic by hand if the operation fails partway. The service has
/// all of it in local variables; once the delete has been issued, this may be the only surviving
/// record — the topic itself is gone, so neither the app's topic list nor its detail panel can
/// answer "how many partitions did it have?".
/// </summary>
/// <param name="PreservedConfig">
/// Topic-level config overrides read before the delete. Trustworthy whenever
/// <see cref="TopicRecreateFailedException.Stage"/> is past <see cref="TopicRecreateStage.ReadingConfig"/>,
/// because the delete is only issued after that read succeeds.
/// </param>
public sealed record TopicRecreateAttempt(
    string TopicName,
    int OriginalPartitionCount,
    int RequestedPartitionCount,
    short ReplicationFactor,
    IReadOnlyDictionary<string, string> PreservedConfig);

/// <summary>
/// Thrown when recreating a topic fails. Carries the stage that failed, whether the user's data is
/// actually at risk, and everything needed to rebuild the topic by hand.
/// </summary>
public class TopicRecreateFailedException : Exception
{
    public TopicRecreateStage Stage { get; }

    /// <summary>
    /// Whether the topic may already be gone.
    /// <para>
    /// Deliberately NOT derived from <see cref="Stage"/>. A delete that the broker refused —
    /// an ACL denial, or <c>delete.topic.enable=false</c> — reaches the controller and comes back
    /// with a per-topic error precisely because it was not executed, so the topic is intact. Only a
    /// local failure (timeout, transport) leaves the outcome genuinely unknown.
    /// </para>
    /// <para>
    /// The distinction is worth the extra field: a maximum-severity data-loss warning that fires
    /// routinely for permission errors teaches the operator to dismiss it, and they will then
    /// dismiss it in the stages where it is true and is the only thing that saves them.
    /// </para>
    /// </summary>
    public bool TopicMayBeDeleted { get; }

    public TopicRecreateAttempt Attempt { get; }

    public string TopicName => Attempt.TopicName;
    public IReadOnlyDictionary<string, string> PreservedConfig => Attempt.PreservedConfig;

    public TopicRecreateFailedException(
        TopicRecreateStage stage,
        bool topicMayBeDeleted,
        TopicRecreateAttempt attempt,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Stage = stage;
        TopicMayBeDeleted = topicMayBeDeleted;
        Attempt = attempt;
    }
}
