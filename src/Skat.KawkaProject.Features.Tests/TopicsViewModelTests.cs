using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Topics.ViewModels;

namespace Skat.KawkaProject.Features.Tests;

public class TopicsViewModelTests
{
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

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object);
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

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object);
        await vm.LoadTopicsAsync();
        await vm.DeleteTopicAsync("to-delete");

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "to-delete"), Times.Once);
    }
}
