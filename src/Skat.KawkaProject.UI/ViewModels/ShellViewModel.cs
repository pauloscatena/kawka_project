using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.UI.ViewModels;

public class ShellViewModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new();
    public SidebarViewModel Sidebar { get; }

    public ShellViewModel(
        IConnectionProfileRepository profileRepo,
        IKafkaConnectionFactory connectionFactory,
        ITopicService topicService,
        IMessageService messageService,
        IClusterService clusterService)
    {
        Sidebar = new SidebarViewModel(this, profileRepo, connectionFactory,
            topicService, messageService, clusterService);
    }
}
