using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Topics.ViewModels;

namespace Skat.KawkaProject.Features.Tests;

public class TopicsViewModelTests
{
    private static readonly Action<string, int> NoOpNavigate = (_, _) => { };

    private static IScreen FakeScreen()
    {
        var mock = new Mock<IScreen>();
        mock.Setup(s => s.Router).Returns(new RoutingState());
        return mock.Object;
    }

    private static IKafkaSession FakeSession()
    {
        var mock = new Mock<IKafkaSession>();
        mock.Setup(s => s.ProfileName).Returns("test");
        return mock.Object;
    }

    [Fact]
    public async Task LoadTopicsAsync_populates_Topics_collection()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 3, 1) });

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();

        Assert.Single(vm.Topics);
        Assert.Equal("orders", vm.Topics[0].Name);
    }

    [Fact]
    public async Task DeleteTopic_removes_from_Topics_collection()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("to-delete", 1, 1) });
        svc.Setup(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "to-delete"))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        vm.ConfirmDelete.RegisterHandler(ctx => ctx.SetOutput(true));
        await vm.LoadTopicsAsync();
        await vm.DeleteTopicAsync("to-delete");

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "to-delete"), Times.Once);
    }

    [Fact]
    public async Task ViewPartitionMessages_invokes_callback_with_selected_topic_and_partition()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(
               new TopicInfo("orders", 2, 1),
               new List<PartitionInfo> { new(0, 1, 0, 10), new(1, 1, 0, 5) }));

        string? capturedTopic = null;
        int? capturedPartition = null;
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object,
            (topic, partition) => { capturedTopic = topic; capturedPartition = partition; });

        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ViewPartitionMessages(1);

        Assert.Equal("orders", capturedTopic);
        Assert.Equal(1, capturedPartition);
    }

    [Fact]
    public void ViewPartitionMessages_does_nothing_when_no_topic_selected()
    {
        var svc = new Mock<ITopicService>();
        var called = false;
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object,
            (_, _) => called = true);

        vm.ViewPartitionMessages(0);

        Assert.False(called);
    }

    [Fact]
    public async Task ExpandPartitionsAsync_calls_service_and_reloads_detail()
    {
        var svc = new Mock<ITopicService>();
        var detailBefore = new TopicDetail(new TopicInfo("orders", 2, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0) });
        var detailAfter = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });

        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.SetupSequence(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(detailBefore)
           .ReturnsAsync(detailAfter);
        svc.Setup(s => s.ExpandPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 4))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.NewPartitionCount = 4;

        await vm.ExpandPartitionsAsync();

        svc.Verify(s => s.ExpandPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 4), Times.Once);
        Assert.Equal(4, vm.SelectedTopicDetail!.Topic.PartitionCount);
    }

    [Fact]
    public async Task ExpandPartitionsAsync_rejects_count_not_greater_than_current()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 2, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(detail);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.NewPartitionCount = 2;

        await vm.ExpandPartitionsAsync();

        svc.Verify(s => s.ExpandPartitionsAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task RecreateTopicAsync_calls_service_when_confirmed_and_in_range()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(detail);
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1), Times.Once);
    }

    [Fact]
    public async Task RecreateTopicAsync_does_nothing_when_confirm_name_does_not_match()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(detail);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "wrong-name";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.RecreateTopicWithFewerPartitionsAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>()), Times.Never);
    }

    private static TopicRecreateAttempt FailedAttempt() => new(
        "orders", OriginalPartitionCount: 4, RequestedPartitionCount: 2, ReplicationFactor: 3,
        PreservedConfig: new Dictionary<string, string> { ["retention.ms"] = "604800000" });

    private static Mock<ITopicService> ServiceThatFailsRecreateWith(Exception failure)
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 3) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 3),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, (short)3))
           .ThrowsAsync(failure);
        return svc;
    }

    private static async Task<TopicsViewModel> RecreateAndReturnVm(Mock<ITopicService> svc)
    {
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];

        // Open the form the way the user does: ShowRecreateFormCommand clears the confirmation
        // name, so setting it first would be silently undone and the assertions about the form's
        // state after a failure would be testing a form that was never open.
        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;
        await vm.RecreateTopicAsync();
        return vm;
    }

    [Theory]
    [InlineData(TopicRecreateStage.Deleting)]
    [InlineData(TopicRecreateStage.WaitingForDeletion)]
    [InlineData(TopicRecreateStage.Creating)]
    public async Task Every_failure_that_may_have_deleted_the_topic_warns_about_data_loss(TopicRecreateStage stage)
    {
        // The likeliest failure is a propagation timeout, where the topic is STILL LISTED at the
        // moment of failure. The old code read that as "nothing happened" and said only "timed
        // out"; the user goes to lunch and the deletion completes behind them.
        var svc = ServiceThatFailsRecreateWith(new TopicRecreateFailedException(
            stage, topicMayBeDeleted: true, FailedAttempt(),
            "the service explains what went wrong", new InvalidOperationException("broker unreachable")));

        var vm = await RecreateAndReturnVm(svc);

        Assert.Contains("DATA LOSS RISK", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_delete_the_broker_refused_does_not_warn_about_data_loss()
    {
        // delete.topic.enable=false or an ACL denial: the topic is provably intact. Crying wolf
        // here teaches the user to dismiss the warning when it is real.
        var svc = ServiceThatFailsRecreateWith(new TopicRecreateFailedException(
            TopicRecreateStage.Deleting, topicMayBeDeleted: false, FailedAttempt(),
            "The cluster refused to delete topic 'orders'. It was NOT modified.",
            new InvalidOperationException("Broker: Invalid request")));

        var vm = await RecreateAndReturnVm(svc);

        Assert.DoesNotContain("DATA LOSS RISK", vm.ErrorMessage);
        Assert.Contains("NOT modified", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_data_loss_warning_carries_everything_needed_to_rebuild_the_topic()
    {
        var svc = ServiceThatFailsRecreateWith(new TopicRecreateFailedException(
            TopicRecreateStage.Creating, topicMayBeDeleted: true, FailedAttempt(),
            "Topic 'orders' was deleted but could not be recreated: rf too large",
            new InvalidOperationException("rf too large")));

        var vm = await RecreateAndReturnVm(svc);

        // The topic is gone, so neither the list nor the detail panel can answer "what was it?".
        // This message is the only surviving record the user has.
        Assert.Contains("4 partitions", vm.ErrorMessage);
        Assert.Contains("replication factor 3", vm.ErrorMessage);
        Assert.Contains("retention.ms=604800000", vm.ErrorMessage);

        // And the reason must survive too, or the user cannot act on it.
        Assert.Contains("rf too large", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_successful_recreate_reselects_the_refreshed_topic()
    {
        // Mutable cluster state rather than SetupSequence: the VM constructor fires a
        // fire-and-forget LoadTopicsAsync, so a sequence is already partly consumed before the
        // test body runs and the assertions end up describing the wrong phase.
        var clusterTopics = new[] { new TopicInfo("orders", 4, 1) };
        var clusterDetail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });

        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(() => clusterTopics);
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(() => clusterDetail);
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, (short)1))
           .Callback(() =>
           {
               clusterTopics = new[] { new TopicInfo("orders", 2, 1) };
               clusterDetail = new TopicDetail(new TopicInfo("orders", 2, 1),
                   new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0) });
           })
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        Assert.Equal(4, vm.SelectedTopic!.PartitionCount);

        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        // ApplyFilter clears the ObservableCollection, so the ListBox writes null back through the
        // two-way binding; the re-added TopicInfo is a different record value (the partition count
        // changed) so it is never auto-reselected. Without an explicit reselect the detail panel
        // stays open with SelectedTopic null.
        Assert.NotNull(vm.SelectedTopic);
        Assert.Equal("orders", vm.SelectedTopic!.Name);
        Assert.Equal(2, vm.SelectedTopic.PartitionCount);
    }

    [Fact]
    public async Task A_failed_recreate_drops_the_topic_from_the_list_when_it_is_really_gone()
    {
        // The recreate destroys the topic and then fails, so the cluster really is left without it.
        var clusterTopics = new[] { new TopicInfo("orders", 4, 3) };

        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(() => clusterTopics);
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 3),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, (short)3))
           .Callback(() => clusterTopics = Array.Empty<TopicInfo>())
           .ThrowsAsync(new TopicRecreateFailedException(
               TopicRecreateStage.Creating, topicMayBeDeleted: true, FailedAttempt(),
               "Topic 'orders' was deleted but could not be recreated: broker down.",
               new InvalidOperationException("broker down")));

        var vm = await RecreateAndReturnVm(svc);

        // The old code fetched the list purely to compute a bool and threw the result away, so the
        // UI kept offering Delete / Expand / Recreate on a topic that no longer existed.
        Assert.Empty(vm.Topics);
        Assert.Null(vm.SelectedTopicDetail);
        Assert.Null(vm.SelectedTopic);
        Assert.Contains("DATA LOSS RISK", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failed_recreate_keeps_the_topic_selected_when_it_survived()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 3) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 3),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, (short)3))
           .ThrowsAsync(new TopicRecreateFailedException(
               TopicRecreateStage.WaitingForDeletion, topicMayBeDeleted: true, FailedAttempt(),
               "Deletion was accepted but could not be confirmed in time.",
               new TimeoutException("timed out")));

        var vm = await RecreateAndReturnVm(svc);

        // Deletion had not propagated: the topic is still listed. The warning stands, but the UI
        // must not pretend the topic vanished.
        Assert.Single(vm.Topics);
        Assert.Equal("orders", vm.SelectedTopic!.Name);
    }

    [Fact]
    public async Task A_topic_with_no_overrides_says_so_instead_of_trailing_off()
    {
        // The common case: most topics override nothing. An empty list must read as "none", not as
        // an empty string that is indistinguishable from "we failed to capture them".
        var attempt = new TopicRecreateAttempt(
            "orders", 4, 2, 3, new Dictionary<string, string>());

        var svc = ServiceThatFailsRecreateWith(new TopicRecreateFailedException(
            TopicRecreateStage.Creating, topicMayBeDeleted: true, attempt,
            "Topic 'orders' was deleted but could not be recreated: broker down.",
            new InvalidOperationException("broker down")));

        var vm = await RecreateAndReturnVm(svc);

        Assert.Contains("config overrides: none", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_data_loss_failure_closes_the_recreate_form()
    {
        var svc = ServiceThatFailsRecreateWith(new TopicRecreateFailedException(
            TopicRecreateStage.Creating, topicMayBeDeleted: true, FailedAttempt(),
            "Topic 'orders' was deleted but could not be recreated: broker down.",
            new InvalidOperationException("broker down")));

        var vm = await RecreateAndReturnVm(svc);

        // Leaving the form open leaves a primed destructive button next to an already-typed
        // confirmation name — and the next click wipes the only surviving record of the topic.
        Assert.False(vm.IsRecreatingTopic);
    }

    [Fact]
    public async Task A_refused_delete_leaves_the_recreate_form_open_to_retry()
    {
        var svc = ServiceThatFailsRecreateWith(new TopicRecreateFailedException(
            TopicRecreateStage.Deleting, topicMayBeDeleted: false, FailedAttempt(),
            "The cluster refused to delete topic 'orders'. It was NOT modified.",
            new InvalidOperationException("Broker: Topic authorization failed")));

        var vm = await RecreateAndReturnVm(svc);

        // Nothing happened, so retrying after fixing the permission is the reasonable next step.
        Assert.True(vm.IsRecreatingTopic);
    }

    [Fact]
    public async Task A_failure_before_anything_was_deleted_is_reported_plainly()
    {
        var svc = ServiceThatFailsRecreateWith(
            new InvalidOperationException("Topic 'orders' has a single partition; there is nothing to reduce."));

        var vm = await RecreateAndReturnVm(svc);

        Assert.DoesNotContain("DATA LOSS RISK", vm.ErrorMessage);
        Assert.Contains("nothing to reduce", vm.ErrorMessage);
    }

    [Fact]
    public async Task Selecting_a_topic_whose_detail_fails_clears_the_stale_detail()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1), new TopicInfo("gone", 1, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0) }));
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "gone"))
           .ThrowsAsync(new InvalidOperationException("Topic 'gone' was not found on the cluster."));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        Assert.NotNull(vm.SelectedTopicDetail);

        vm.SelectedTopic = vm.Topics[1];

        // Leaving 'orders' in the panel while the list highlights 'gone' makes the panel's own
        // buttons target two different topics: expand/recreate read SelectedTopicDetail, delete
        // reads SelectedTopic.
        Assert.Null(vm.SelectedTopicDetail);
        Assert.Contains("not found", vm.ErrorMessage);
    }

    [Fact]
    public async Task RecreateTopicAsync_rejects_count_outside_valid_range()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(detail);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 4;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.RecreateTopicWithFewerPartitionsAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>()), Times.Never);
        Assert.NotNull(vm.ErrorMessage);
    }
}
