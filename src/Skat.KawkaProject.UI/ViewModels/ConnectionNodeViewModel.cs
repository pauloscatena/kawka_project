using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.UI.ViewModels;

public enum ConnectionStatus { Disconnected, Connecting, Connected, Error }

public class ConnectionNodeViewModel : ReactiveObject
{
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private string? _errorMessage;
    private IKafkaSession? _session;

    public ConnectionProfile Profile { get; }
    public string Name => Profile.Name;

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(IsDisconnected));
            this.RaisePropertyChanged(nameof(StatusLabel));
        }
    }

    public bool IsConnected => _status == ConnectionStatus.Connected;
    public bool IsDisconnected => _status != ConnectionStatus.Connected;

    public string StatusLabel => _status switch
    {
        ConnectionStatus.Connected => "live",
        ConnectionStatus.Connecting => "conn…",
        ConnectionStatus.Error => "error",
        _ => "off"
    };

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand NavigateToTopicsCommand { get; }
    public ICommand NavigateToMessagesCommand { get; }
    public ICommand NavigateToClusterCommand { get; }
    public ICommand DeleteCommand { get; }

    public ConnectionNodeViewModel(
        ConnectionProfile profile,
        IScreen shell,
        IKafkaConnectionFactory factory,
        ITopicService topicService,
        IMessageService messageService,
        IClusterService clusterService,
        Action<ConnectionNodeViewModel> onDelete)
    {
        Profile = profile;

        ConnectCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            Status = ConnectionStatus.Connecting;
            ErrorMessage = null;
            try
            {
                _session = await factory.ConnectAsync(Profile);
                Status = ConnectionStatus.Connected;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Status = ConnectionStatus.Error;
            }
        });

        DisconnectCommand = ReactiveCommand.Create(() =>
        {
            _session?.Dispose();
            _session = null;
            Status = ConnectionStatus.Disconnected;
        });

        NavigateToTopicsCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Topics.ViewModels.TopicsViewModel(
                    shell, _session, topicService, ShowPartitionMessages));
        });

        void ShowPartitionMessages(string topicName, int partitionId)
        {
            // Deferred null-check, NOT a copy of the one guarding NavigateToTopicsCommand above:
            // this runs when the user clicks a partition's eye icon, which may be minutes later -
            // long enough for DisconnectCommand to have nulled _session out from under this lambda.
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                Skat.KawkaProject.Features.Messages.ViewModels.MessagesViewModel.ForPartition(
                    shell, _session, messageService, topicService, topicName, partitionId));
        }

        NavigateToMessagesCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Messages.ViewModels.MessagesViewModel(shell, _session, messageService, topicService));
        });

        NavigateToClusterCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Cluster.ViewModels.ClusterViewModel(shell, _session, clusterService));
        });

        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
    }
}
