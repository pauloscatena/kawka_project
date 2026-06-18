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
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

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

        // Navigation commands wired in Tasks 8-10 when feature projects are added
        NavigateToTopicsCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
        });

        NavigateToMessagesCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
        });

        NavigateToClusterCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
        });

        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
    }
}
