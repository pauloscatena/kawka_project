using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Topics.ViewModels;

namespace Skat.KawkaProject.Features.Tests;

public class TopicsViewModelTests
{
    // NOTE for anyone adding a delayed mock: assigning vm.SelectedTopic starts a fire-and-forget
    // LoadDetailAsync (the SelectedTopic setter), and these tests rely on SelectedTopicDetail being
    // populated by the next line. That only holds because Moq's ReturnsAsync hands back an
    // already-completed task and the continuation runs inline. Give GetTopicDetailAsync a real
    // delay and tests start seeing a null SelectedTopicDetail for reasons invisible in the body.
    // Prefer mutable state via ReturnsAsync(() => ...) + Callback over SetupSequence: the
    // constructor's own fire-and-forget LoadTopicsAsync consumes one step of a sequence before the
    // test body runs, so a sequence describes the wrong phase.
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
        Assert.Contains(vm.Topics, t => t.Name == "to-delete");

        await vm.DeleteTopicAsync("to-delete");

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "to-delete"), Times.Once);
        // The name promises the collection is updated, not just that the service was called: without
        // this the RemoveAll + ApplyFilter in DeleteTopicAsync could be deleted and the test stayed green.
        Assert.DoesNotContain(vm.Topics, t => t.Name == "to-delete");
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
        vm.ExpandToPartitionCount = 4;

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
        vm.ExpandToPartitionCount = 2;

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
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2), Times.Once);
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

        svc.Verify(s => s.DeleteAndRecreateTopicAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
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
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
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
    public async Task Opening_one_inline_form_closes_the_others()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];

        vm.ShowExpandFormCommand.Execute(null);
        Assert.True(vm.IsExpandingPartitions);
        Assert.False(vm.IsRecreatingTopic);
        Assert.False(vm.IsCreatingTopic);

        // The forms share one DockPanel; two open at once put two "New count:" inputs on the panel
        // whose whole job is to make a destructive operation unambiguous.
        vm.ShowRecreateFormCommand.Execute(null);
        Assert.True(vm.IsRecreatingTopic);
        Assert.False(vm.IsExpandingPartitions);

        vm.ShowCreateFormCommand.Execute(null);
        Assert.True(vm.IsCreatingTopic);
        Assert.False(vm.IsRecreatingTopic);
        Assert.False(vm.IsExpandingPartitions);
    }

    [Fact]
    public void The_IsNot_form_flags_are_the_negation_of_their_form()
    {
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), Mock.Of<ITopicService>(), NoOpNavigate);

        Assert.True(vm.IsNotCreatingTopic);
        Assert.True(vm.IsNotExpandingPartitions);
        Assert.True(vm.IsNotRecreatingTopic);

        vm.ShowExpandFormCommand.Execute(null);
        Assert.False(vm.IsNotExpandingPartitions);
        Assert.True(vm.IsNotRecreatingTopic);
        Assert.True(vm.IsNotCreatingTopic);
    }

    [Fact]
    public async Task IsBusy_stays_true_across_the_whole_successful_recreate_including_the_reload()
    {
        var detail4 = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });

        // The post-recreate detail load is gated so we can inspect IsBusy while the operation is
        // still in flight, AFTER the internal reload has run.
        var detailGate = new TaskCompletionSource<TopicDetail>();
        Func<Task<TopicDetail>> detailProvider = () => Task.FromResult(detail4);

        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .Returns(() => detailProvider());
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        // From here on, the detail load blocks on the gate.
        detailProvider = () => detailGate.Task;
        var running = vm.RecreateTopicAsync();

        // The reload (LoadTopicsAsync) has run; the detail load is now suspended on the gate. The
        // whole point of the gating this branch introduced is that NOTHING is clickable until the
        // operation finishes. If the nested LoadTopicsAsync's own finally cleared IsBusy, the UI
        // re-enabled mid-operation - the delete button goes live on the just-recreated topic and a
        // concurrent selection can desync panel from list.
        Assert.False(vm.IsNotBusy);

        detailGate.SetResult(detail4);
        await running;

        Assert.True(vm.IsNotBusy);
    }

    [Fact]
    public async Task IsNotBusy_tracks_IsBusy_and_notifies_while_an_operation_is_in_flight()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));

        var gate = new TaskCompletionSource();
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .Returns(gate.Task);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var running = vm.RecreateTopicAsync();

        // Every mutating control binds IsEnabled to IsNotBusy, so a 30s recreate must not leave the
        // delete button live on the same topic.
        Assert.False(vm.IsNotBusy);

        gate.SetResult();
        await running;

        Assert.True(vm.IsNotBusy);
        Assert.Contains(nameof(TopicsViewModel.IsNotBusy), raised);
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
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
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
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
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
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
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
    public async Task RecreateTopicAsync_refuses_an_empty_partition_count_instead_of_using_a_stale_one()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "orders";

        // The box was cleared. With a non-nullable int the binding write silently failed and the VM
        // kept the pre-filled value, so a destructive recreate ran with a count the user never chose
        // and could not see on screen.
        vm.RecreatePartitionCount = null;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.DeleteAndRecreateTopicAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.Contains("Enter", vm.ErrorMessage);
    }

    [Fact]
    public async Task RecreateTopicAsync_explains_a_single_partition_topic_cannot_shrink()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("solo", 1, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "solo"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("solo", 1, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0) }));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "solo";

        await vm.RecreateTopicAsync();

        // The user reads a warning, carefully types the topic name, clicks — and the old code
        // answered "must be between 1 and 0", a nonsense range at the worst possible moment.
        Assert.DoesNotContain("between 1 and 0", vm.ErrorMessage);
        Assert.Contains("nothing to reduce", vm.ErrorMessage);
        svc.Verify(s => s.DeleteAndRecreateTopicAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExpandPartitionsAsync_refuses_an_empty_partition_count()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 2, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0) }));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ShowExpandFormCommand.Execute(null);
        vm.ExpandToPartitionCount = null;

        await vm.ExpandPartitionsAsync();

        svc.Verify(s => s.ExpandPartitionsAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.Contains("Enter", vm.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]    // below the minimum
    [InlineData(-1)]   // negative
    [InlineData(4)]    // equal to current - not fewer
    [InlineData(5)]    // above current
    public async Task RecreateTopicAsync_rejects_a_count_outside_1_to_current_minus_1(int requested)
    {
        // The original test only exercised the upper bound (4 vs 4), so tightening the guard to
        // reject 0 would have kept it green while breaking nothing it claimed to cover. The lower
        // bounds are not reachable through NumericUpDown's Minimum="1", but the guard is
        // defense-in-depth and a second caller (a future CLI) is not bound by the spinner.
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(detail);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = requested;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.DeleteAndRecreateTopicAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_pre_delete_argument_error_from_the_service_is_shown_without_dotnet_jargon()
    {
        // The VM's own range check passes against a stale detail (the panel still shows 4
        // partitions), the service re-reads the live topic and refuses. That refusal is an
        // ArgumentOutOfRangeException, whose Message carries framework tails the user should
        // never read in a banner the rest of the app writes by hand.
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .ThrowsAsync(new ArgumentOutOfRangeException("newPartitionCount", 2,
               "Must be between 1 and 1: topic 'orders' currently has 2 partitions, and this operation only reduces the partition count."));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        Assert.Contains("Must be between 1 and 1", vm.ErrorMessage);
        Assert.DoesNotContain("Parameter", vm.ErrorMessage);
        Assert.DoesNotContain("Actual value was", vm.ErrorMessage);
        Assert.DoesNotContain("DATA LOSS RISK", vm.ErrorMessage);   // nothing was deleted
    }

    [Fact]
    public void The_recreate_warning_reads_its_consequences_from_the_canonical_list()
    {
        // The warning panel binds to these two properties. Restating the consequences in XAML - or
        // rewording them here - is how the GUI, the ITopicService contract and the planned TUI
        // drifted into three lists that could disagree about what a recreate destroys.
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), Mock.Of<ITopicService>(), NoOpNavigate);

        // Every consequence has to reach the panel through one of the two lines. Asserting the
        // union rather than each line's own filter is the point: a filter that starts dropping
        // something no other line picks up fails here, which asserting "RecreateAdditionalLosses
        // contains everything except LostMessages" could never do - that just restates the filter.
        var panel = vm.RecreateHeadline + " " + vm.RecreateAdditionalLosses;
        foreach (var loss in DestructiveAction.RecreateLoses)
            Assert.Contains(loss, panel);
        foreach (var kept in DestructiveAction.RecreatePreserves)
            Assert.Contains(kept, vm.RecreateWhatIsPreserved);

        // The message loss belongs to the headline alone - stated once, in red, not echoed in body
        // text directly below it.
        Assert.Contains(DestructiveAction.LostMessages, vm.RecreateHeadline);
        Assert.DoesNotContain(DestructiveAction.LostMessages, vm.RecreateAdditionalLosses);

        // The user is told which setting decides between skipping and replaying, not just that one
        // of the two will happen.
        Assert.Contains("auto.offset.reset", vm.RecreateAdditionalLosses);

        // The halves must not be swapped: config overrides survive, messages do not.
        Assert.DoesNotContain("config", vm.RecreateAdditionalLosses, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("message", vm.RecreateWhatIsPreserved, StringComparison.OrdinalIgnoreCase);
    }
}
