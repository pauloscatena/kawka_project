using System.Reactive.Subjects;
using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Messages.ViewModels;

namespace Skat.KawkaProject.Features.Tests;

public class MessagesViewModelTests
{
    private static IScreen FakeScreen()
    {
        var mock = new Mock<IScreen>();
        mock.Setup(s => s.Router).Returns(new RoutingState());
        return mock.Object;
    }

    private static IKafkaSession FakeSession() => new Mock<IKafkaSession>().Object;

    private static KafkaMessage Msg(string value) =>
        new("topic", 0, 0, null, value, DateTime.UtcNow);

    [Fact]
    public async Task FetchMessages_populates_Messages_in_offset_mode()
    {
        var svc = new Mock<IMessageService>();
        svc.Setup(s => s.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "t", 0, 0, 50))
           .ReturnsAsync(new[] { Msg("hello"), Msg("world") });

        var vm = new MessagesViewModel(FakeScreen(), FakeSession(), svc.Object, new Mock<ITopicService>().Object);
        vm.TopicName = "t";
        await vm.FetchMessagesAsync();

        Assert.Equal(2, vm.Messages.Count);
    }

    [Fact]
    public void StartTail_adds_live_messages_to_top_of_collection()
    {
        var subject = new Subject<KafkaMessage>();
        var svc = new Mock<IMessageService>();
        svc.Setup(s => s.Tail(It.IsAny<IKafkaSession>(), It.IsAny<string>()))
           .Returns(subject);

        var vm = new MessagesViewModel(FakeScreen(), FakeSession(), svc.Object, new Mock<ITopicService>().Object);
        vm.TopicName = "live-topic";
        vm.StartTail();

        subject.OnNext(Msg("first"));
        subject.OnNext(Msg("second"));

        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal("second", vm.Messages[0].Value);
    }

    [Fact]
    public async Task ForPartition_points_the_view_model_at_one_partition_and_starts_fetching()
    {
        var svc = new Mock<IMessageService>();
        var fetched = new TaskCompletionSource<IEnumerable<KafkaMessage>>();
        svc.Setup(s => s.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 3, It.IsAny<long>(), It.IsAny<int>()))
           .Returns(fetched.Task);

        var vm = MessagesViewModel.ForPartition(
            FakeScreen(), FakeSession(), svc.Object, new Mock<ITopicService>().Object,
            topicName: "orders", partitionId: 3);

        // The factory owns the whole init protocol - the previous callers had to remember all four
        // steps (topic, partition, offset mode, fetch) at every call site.
        Assert.Equal("orders", vm.TopicName);
        Assert.Equal(3, vm.Partition);
        Assert.Equal(MessageMode.Offset, vm.Mode);

        // And it started the fetch: the service was asked for this partition's messages.
        svc.Verify(s => s.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 3, It.IsAny<long>(), It.IsAny<int>()),
            Times.Once);

        fetched.SetResult(Array.Empty<KafkaMessage>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ForPartition_refuses_an_empty_topic_name(string? topicName)
    {
        // ThrowsAny: null yields ArgumentNullException, empty/whitespace yields ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => MessagesViewModel.ForPartition(
            FakeScreen(), FakeSession(), new Mock<IMessageService>().Object, new Mock<ITopicService>().Object,
            topicName!, partitionId: 0));
    }

    [Fact]
    public void PauseTail_stops_adding_messages_without_unsubscribing()
    {
        var subject = new Subject<KafkaMessage>();
        var svc = new Mock<IMessageService>();
        svc.Setup(s => s.Tail(It.IsAny<IKafkaSession>(), It.IsAny<string>()))
           .Returns(subject);

        var vm = new MessagesViewModel(FakeScreen(), FakeSession(), svc.Object, new Mock<ITopicService>().Object);
        vm.TopicName = "paused-topic";
        vm.StartTail();
        vm.PauseTail();

        subject.OnNext(Msg("while-paused"));

        Assert.Empty(vm.Messages);
    }
}
