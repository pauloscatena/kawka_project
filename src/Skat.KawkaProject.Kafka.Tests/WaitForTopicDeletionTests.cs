using Skat.KawkaProject.Kafka;

namespace Skat.KawkaProject.Kafka.Tests;

/// <summary>
/// Unit tests for the deletion wait loop, driven through the internal seam with scripted answers
/// and zero delays. These do NOT need a broker.
///
/// They exist because the loop's whole reason for being — refusing to act on a single "the topic
/// is gone" reading — is invisible to the integration suite: every one of those tests talks to a
/// healthy single-node broker that answers consistently, so weakening or deleting the guard leaves
/// them all green.
/// </summary>
public class WaitForTopicDeletionTests
{
    private static readonly TimeSpan NoDelay = TimeSpan.Zero;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>Returns answers in order; throws if the loop asks for more than were scripted.</summary>
    private static Func<Task<bool>> Scripted(Queue<bool> answers, Action? onCall = null) => () =>
    {
        onCall?.Invoke();
        return Task.FromResult(answers.Dequeue());
    };

    [Fact]
    public async Task A_single_absence_between_two_sightings_does_not_end_the_wait()
    {
        // absent, present, absent, absent — the lone absence at #1 is exactly the degenerate
        // metadata answer the guard exists to survive.
        var answers = new Queue<bool>(new[] { true, false, true, true });
        var calls = 0;

        await TopicRecreateOperation.WaitForTopicDeletionAsync(
            "t", Scripted(answers, () => calls++), NoDelay, NoDelay, Budget);

        Assert.Equal(4, calls);
        Assert.Empty(answers);
    }

    [Fact]
    public async Task Two_consecutive_absences_end_the_wait()
    {
        var answers = new Queue<bool>(new[] { true, true });
        var calls = 0;

        await TopicRecreateOperation.WaitForTopicDeletionAsync(
            "t", Scripted(answers, () => calls++), NoDelay, NoDelay, Budget);

        // Exactly two: it must not keep polling after the second consecutive absence.
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_absence_observed_as_the_budget_expires_is_confirmed_rather_than_timing_out()
    {
        var calls = 0;

        // One in-loop poll fits inside the budget and sees the absence; the poll delay then pushes
        // past the deadline. Without the post-loop confirmation this throws TimeoutException for a
        // deletion that actually completed — and downstream that becomes a data-loss warning shown
        // to a user whose topic is fine.
        await TopicRecreateOperation.WaitForTopicDeletionAsync(
            "t",
            () => { calls++; return Task.FromResult(true); },
            grace: NoDelay,
            pollInterval: TimeSpan.FromMilliseconds(40),
            timeout: TimeSpan.FromMilliseconds(15));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_topic_that_never_disappears_times_out()
    {
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            TopicRecreateOperation.WaitForTopicDeletionAsync(
                "stubborn",
                () => Task.FromResult(false),
                grace: NoDelay,
                pollInterval: TimeSpan.FromMilliseconds(5),
                timeout: TimeSpan.FromMilliseconds(40)));

        Assert.Contains("stubborn", ex.Message);
        Assert.Contains("kept reporting the topic", ex.Message);
    }

    [Fact]
    public async Task A_probe_error_resets_the_absence_streak()
    {
        // absent, throw, absent — the error must invalidate the streak, so this is NOT two
        // consecutive absences and the loop keeps going.
        var step = 0;
        var calls = 0;

        Task<bool> Probe()
        {
            calls++;
            return (step++) switch
            {
                0 => Task.FromResult(true),
                1 => throw new InvalidOperationException("metadata unavailable"),
                _ => Task.FromResult(true)
            };
        }

        await TopicRecreateOperation.WaitForTopicDeletionAsync("t", Probe, NoDelay, NoDelay, Budget);

        // 1 absent, 2 throws (streak reset), 3 absent, 4 absent -> returns on the fourth.
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task A_probe_error_that_later_recovers_is_not_blamed_for_a_timeout()
    {
        var step = 0;

        Task<bool> Probe()
        {
            // Fails once, then reports the topic present forever: the real cause of the timeout is
            // "the deletion never propagated", not the transient error at the start.
            if (step++ == 0) throw new InvalidOperationException("leader not available");
            return Task.FromResult(false);
        }

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            TopicRecreateOperation.WaitForTopicDeletionAsync(
                "t", Probe, NoDelay, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(60)));

        Assert.DoesNotContain("leader not available", ex.Message);
        Assert.Null(ex.InnerException);
    }
}
