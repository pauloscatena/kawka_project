using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

/// <summary>
/// Unit tests for how each stage of the recreate translates a failure.
///
/// These exist because the translations are safety statements about the user's data and a healthy
/// broker never exercises them: mislabelling the Deleting or WaitingForDeletion stage — which would
/// suppress the data-loss warning in the two most likely failure modes — leaves the entire
/// integration suite green.
/// </summary>
public class RecreateStageTests
{
    private static readonly TopicRecreateAttempt Attempt = new(
        "orders", OriginalPartitionCount: 4, RequestedPartitionCount: 2, ReplicationFactor: 1,
        PreservedConfig: new Dictionary<string, string> { ["retention.ms"] = "604800000" });

    private static Task Ok() => Task.CompletedTask;
    private static Task Boom(Exception ex) => Task.FromException(ex);

    private static DeleteTopicsException BrokerRefused(ErrorCode code, string reason) =>
        new(new List<DeleteTopicReport>
        {
            new() { Topic = "orders", Error = new Error(code, reason) }
        });

    [Fact]
    public async Task A_delete_the_broker_refused_is_reported_as_NOT_destructive()
    {
        // delete.topic.enable=false, or an ACL denial. The controller answered with a per-topic
        // error precisely because it did not execute the delete, so the topic is intact.
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.RunRecreateStagesAsync(
                Attempt,
                deleteTopic: () => Boom(BrokerRefused(ErrorCode.InvalidRequest, "Broker: Invalid request")),
                waitForDeletion: Ok,
                createWithRetry: Ok));

        Assert.Equal(TopicRecreateStage.Deleting, ex.Stage);

        // Firing a maximum-severity data-loss warning for a routine permission error teaches the
        // operator to dismiss it - and then they dismiss it when it is true.
        Assert.False(ex.TopicMayBeDeleted);
        Assert.Contains("NOT modified", ex.Message);
    }

    [Fact]
    public async Task A_delete_that_failed_locally_is_reported_as_possibly_destructive()
    {
        // A timeout or transport failure: the request may have reached the controller and only the
        // response was lost. This is the genuinely ambiguous case, and it must warn.
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.RunRecreateStagesAsync(
                Attempt,
                deleteTopic: () => Boom(new KafkaException(ErrorCode.Local_TimedOut)),
                waitForDeletion: Ok,
                createWithRetry: Ok));

        Assert.Equal(TopicRecreateStage.Deleting, ex.Stage);
        Assert.True(ex.TopicMayBeDeleted);
    }

    [Fact]
    public async Task A_delete_refused_with_a_local_error_code_inside_DeleteTopicsException_still_warns()
    {
        // DeleteTopicsException carrying a LOCAL code is not a broker refusal - the discriminator
        // is the error's origin, not the exception type.
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.RunRecreateStagesAsync(
                Attempt,
                deleteTopic: () => Boom(BrokerRefused(ErrorCode.Local_TimedOut, "Local: Timed out")),
                waitForDeletion: Ok,
                createWithRetry: Ok));

        Assert.True(ex.TopicMayBeDeleted);
    }

    [Fact]
    public async Task A_propagation_timeout_is_reported_as_possibly_destructive()
    {
        // The plan's own motivating scenario: the delete was accepted, propagation is slow, we time
        // out. The topic is on its way out and nothing was put back.
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.RunRecreateStagesAsync(
                Attempt,
                deleteTopic: Ok,
                waitForDeletion: () => Boom(new TimeoutException("did not disappear in time")),
                createWithRetry: Ok));

        Assert.Equal(TopicRecreateStage.WaitingForDeletion, ex.Stage);
        Assert.True(ex.TopicMayBeDeleted);
        Assert.Contains("did not disappear in time", ex.Message);
    }

    [Fact]
    public async Task Every_failure_carries_what_is_needed_to_rebuild_the_topic()
    {
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.RunRecreateStagesAsync(
                Attempt,
                deleteTopic: Ok,
                waitForDeletion: () => Boom(new TimeoutException("nope")),
                createWithRetry: Ok));

        Assert.Equal("orders", ex.TopicName);
        Assert.Equal(4, ex.Attempt.OriginalPartitionCount);
        Assert.Equal("604800000", ex.PreservedConfig["retention.ms"]);
    }

    [Fact]
    public async Task A_clean_run_reaches_the_create_and_throws_nothing()
    {
        var created = false;

        await TopicRecreateOperation.RunRecreateStagesAsync(
            Attempt, deleteTopic: Ok, waitForDeletion: Ok,
            createWithRetry: () => { created = true; return Task.CompletedTask; });

        Assert.True(created);
    }

    [Fact]
    public async Task A_failure_from_the_create_stage_is_passed_through_untouched()
    {
        var original = new TopicRecreateFailedException(
            TopicRecreateStage.Creating, true, Attempt, "already translated", new Exception("inner"));

        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(() =>
            TopicRecreateOperation.RunRecreateStagesAsync(
                Attempt, deleteTopic: Ok, waitForDeletion: Ok, createWithRetry: () => Boom(original)));

        Assert.Same(original, ex);
    }
}
