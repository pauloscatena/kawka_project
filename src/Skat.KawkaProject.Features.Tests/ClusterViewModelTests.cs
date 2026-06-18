using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Cluster.ViewModels;

namespace Skat.KawkaProject.Features.Tests;

public class ClusterViewModelTests
{
    private static IScreen FakeScreen()
    {
        var mock = new Mock<IScreen>();
        mock.Setup(s => s.Router).Returns(new RoutingState());
        return mock.Object;
    }

    private static IKafkaSession FakeSession() => new Mock<IKafkaSession>().Object;

    [Fact]
    public async Task LoadAsync_populates_Brokers()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListBrokersAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new BrokerInfo(1, "localhost", 9092, true) });
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(Array.Empty<ConsumerGroupInfo>());

        var vm = new ClusterViewModel(FakeScreen(), FakeSession(), svc.Object);
        await vm.LoadAsync();

        Assert.Single(vm.Brokers);
        Assert.Equal("localhost", vm.Brokers[0].Host);
    }

    [Fact]
    public async Task LoadLagAsync_populates_Lag_for_selected_group()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListBrokersAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(Array.Empty<BrokerInfo>());
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new ConsumerGroupInfo("my-group", "Stable", 2) });
        svc.Setup(s => s.GetGroupLagAsync(It.IsAny<IKafkaSession>(), "my-group"))
           .ReturnsAsync(new[] { new PartitionLag("orders", 0, 5, 10, 5) });

        var vm = new ClusterViewModel(FakeScreen(), FakeSession(), svc.Object);
        await vm.LoadAsync();
        vm.SelectedGroup = vm.ConsumerGroups[0];
        await vm.LoadLagAsync();

        Assert.Single(vm.Lag);
        Assert.Equal(5, vm.Lag[0].Lag);
    }
}
