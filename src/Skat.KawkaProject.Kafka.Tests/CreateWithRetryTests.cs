using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

/// <summary>
/// Unit tests for the recreate's create-with-retry, driven through the internal seam with scripted
/// failures and no delay. No broker needed.
///
/// This is the step that runs AFTER the topic has already been deleted, so every behaviour here is
/// about not losing the user's topic. None of it is observable through the integration suite: a
/// single-node container cannot be made to fail a create transiently and then succeed, which is
/// precisely the case the retry exists for.
/// </summary>
public class CreateWithRetryTests
{
    private static readonly TimeSpan NoDelay = TimeSpan.Zero;

    private static TopicRecreateAttempt Attempt(string topic = "orders") => new(
        topic, OriginalPartitionCount: 4, RequestedPartitionCount: 2, ReplicationFactor: 1,
        PreservedConfig: new Dictionary<string, string> { ["retention.ms"] = "604800000" });

    private static CreateTopicsException Collision(string topic) =>
        new(new List<CreateTopicReport>
        {
            new() { Topic = topic, Error = new Error(ErrorCode.TopicAlreadyExists, "already exists") }
        });

    private static CreateTopicsException BadReplicationFactor(string topic) =>
        new(new List<CreateTopicReport>
        {
            new() { Topic = topic, Error = new Error(ErrorCode.InvalidReplicationFactor, "rf too large") }
        });

    private static Func<Task<bool>> Matches(bool result) => () => Task.FromResult(result);

    [Fact]
    public void The_production_policy_actually_retries()
    {
        // Every other test here passes its own attempt count, so all of them stay green if the
        // production constants are set to "do not retry" - silently removing the retry from the
        // one call that runs after the user's topic has already been deleted.
        Assert.True(TopicRecreateOperation.CreateAttempts > 1,
            $"CreateAttempts is {TopicRecreateOperation.CreateAttempts}: the recreate would give up on the " +
            "user's topic after a single transient failure.");
        Assert.True(TopicRecreateOperation.CreateRetryDelay > TimeSpan.Zero,
            "Retrying with no delay retries into the same transient failure.");
    }

    [Fact]
    public async Task A_create_that_succeeds_first_time_is_attempted_once()
    {
        var calls = 0;

        await TopicRecreateOperation.CreateWithRetryAsync(
            Attempt(), () => { calls++; return Task.CompletedTask; }, Matches(true), 3, NoDelay);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_until_it_succeeds()
    {
        var calls = 0;

        await TopicRecreateOperation.CreateWithRetryAsync(
            Attempt(),
            () =>
            {
                if (++calls < 3) throw new KafkaException(ErrorCode.Local_Transport);
                return Task.CompletedTask;
            },
            Matches(true), 3, NoDelay);

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Exhausting_the_attempts_reports_the_creating_stage_and_keeps_everything_needed_to_rebuild()
    {
        var calls = 0;

        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.CreateWithRetryAsync(
                Attempt(),
                () => { calls++; throw new KafkaException(ErrorCode.Local_Transport); },
                Matches(false), 3, NoDelay));

        Assert.Equal(3, calls);
        Assert.Equal(TopicRecreateStage.Creating, ex.Stage);
        Assert.True(ex.TopicMayBeDeleted);

        // At this point the topic is gone, so neither the topic list nor the detail panel can tell
        // the user what it used to be. The exception is the only surviving record.
        Assert.Equal("orders", ex.TopicName);
        Assert.Equal(4, ex.Attempt.OriginalPartitionCount);
        Assert.Equal(2, ex.Attempt.RequestedPartitionCount);
        Assert.Equal((short)1, ex.Attempt.ReplicationFactor);
        Assert.Equal("604800000", ex.PreservedConfig["retention.ms"]);

        // The reason must reach the user: the outer message is what a caller reading ex.Message
        // sees, and "could not be recreated after 3 attempts" alone is not actionable.
        Assert.Contains("Local: Broker transport failure", ex.Message);
    }

    [Fact]
    public async Task A_name_collision_is_success_only_when_the_topic_is_the_one_requested()
    {
        var calls = 0;

        // The response to attempt 1 was lost but the topic WAS created, so attempt 2 collides and
        // the cluster confirms 2 partitions - our create did land.
        await TopicRecreateOperation.CreateWithRetryAsync(
            Attempt(),
            () =>
            {
                if (++calls == 1) throw new KafkaException(ErrorCode.Local_Transport);
                throw Collision("orders");
            },
            Matches(true), 3, NoDelay);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_name_collision_with_a_different_topic_is_a_failure_not_a_silent_success()
    {
        // A consumer auto-created 'orders' after the deletion propagated, so the name is taken by a
        // topic with the ORIGINAL partition count. Reporting success here tells the user their
        // shrink happened when it did not - with the data already destroyed.
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.CreateWithRetryAsync(
                Attempt(), () => throw Collision("orders"), Matches(false), 3, NoDelay));

        Assert.Equal(TopicRecreateStage.Creating, ex.Stage);
        Assert.True(ex.TopicMayBeDeleted);
        Assert.Contains("not the one requested", ex.Message);
    }

    [Fact]
    public async Task A_collision_verdict_does_not_depend_on_a_transient_error_happening_first()
    {
        // Same cluster state as the test above, with an unrelated blip in the middle. The verdict
        // must be identical: the earlier heuristic flipped to "success" here because a non-collision
        // error re-armed it.
        var calls = 0;

        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.CreateWithRetryAsync(
                Attempt(),
                () => (++calls) switch
                {
                    1 => throw Collision("orders"),
                    2 => throw new KafkaException(ErrorCode.Local_Transport),
                    _ => throw Collision("orders")
                },
                Matches(false), 3, NoDelay));

        Assert.Equal(TopicRecreateStage.Creating, ex.Stage);
    }

    [Fact]
    public async Task A_probe_that_itself_fails_still_reports_data_loss_rather_than_escaping_untyped()
    {
        // One network blip causes both halves of this: the create's response is lost, so attempt 2
        // collides, and the probe that would resolve the collision hits the same outage.
        //
        // In C# an exception thrown INSIDE a catch block is not caught by that try's sibling
        // catches. Without explicit handling it escapes as a bare KafkaException all the way to the
        // caller, which has no way to know a delete ever happened - the user is shown
        // "Local: Timed out" for a destroyed topic.
        var calls = 0;

        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.CreateWithRetryAsync(
                Attempt(),
                () =>
                {
                    if (++calls == 1) throw new KafkaException(ErrorCode.Local_Transport);
                    throw Collision("orders");
                },
                () => throw new KafkaException(ErrorCode.Local_TimedOut),
                3, NoDelay));

        Assert.Equal(TopicRecreateStage.Creating, ex.Stage);
        Assert.True(ex.TopicMayBeDeleted);
        Assert.Equal("orders", ex.TopicName);
        Assert.Equal("604800000", ex.PreservedConfig["retention.ms"]);
    }

    [Fact]
    public async Task A_deterministic_configuration_error_is_not_retried()
    {
        var calls = 0;

        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.CreateWithRetryAsync(
                Attempt(),
                () => { calls++; throw BadReplicationFactor("orders"); },
                Matches(false), 3, TimeSpan.FromSeconds(2)));

        // Retrying this spends 4 seconds of the user's time, with their topic already deleted, to
        // fail identically. Measured: 4010ms with retries versus 39ms without.
        Assert.Equal(1, calls);
        Assert.Equal(TopicRecreateStage.Creating, ex.Stage);
    }
}
