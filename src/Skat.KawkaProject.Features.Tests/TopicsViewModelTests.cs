using Moq;
using ReactiveUI;
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
