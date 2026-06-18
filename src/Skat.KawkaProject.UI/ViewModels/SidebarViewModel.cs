using System.Collections.ObjectModel;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.UI.ViewModels;

public class SidebarViewModel : ReactiveObject
{
    private readonly IScreen _shell;
    private readonly IConnectionProfileRepository _profileRepo;
    private readonly IKafkaConnectionFactory _connectionFactory;
    private readonly ITopicService _topicService;
    private readonly IMessageService _messageService;
    private readonly IClusterService _clusterService;

    public ObservableCollection<ConnectionNodeViewModel> Connections { get; } = new();
    public ICommand AddConnectionCommand { get; }

    public SidebarViewModel(
        IScreen shell,
        IConnectionProfileRepository profileRepo,
        IKafkaConnectionFactory connectionFactory,
        ITopicService topicService,
        IMessageService messageService,
        IClusterService clusterService)
    {
        _shell = shell;
        _profileRepo = profileRepo;
        _connectionFactory = connectionFactory;
        _topicService = topicService;
        _messageService = messageService;
        _clusterService = clusterService;

        foreach (var profile in profileRepo.GetAll())
            Connections.Add(CreateNode(profile));

        AddConnectionCommand = ReactiveCommand.Create(OpenAddConnectionDialog);
    }

    private void OpenAddConnectionDialog()
    {
        // Wired in Task 7 when Features.Connections is added
    }

    internal void AddProfile(ConnectionProfile profile)
    {
        _profileRepo.Save(profile);
        Connections.Add(CreateNode(profile));
    }

    private ConnectionNodeViewModel CreateNode(ConnectionProfile profile) =>
        new(profile, _shell, _connectionFactory, _topicService, _messageService,
            _clusterService, node =>
            {
                _profileRepo.Delete(node.Profile.Id);
                Connections.Remove(node);
            });
}
