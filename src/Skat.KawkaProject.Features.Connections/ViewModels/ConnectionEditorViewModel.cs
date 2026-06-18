using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Connections.ViewModels;

public class ConnectionEditorViewModel : ReactiveObject
{
    private string _name = "";
    private string _bootstrapServers = "";
    private AuthType _authType = AuthType.None;
    private string? _saslUsername;
    private string? _saslPassword;
    private string? _sslCertPath;
    private string? _sslKeyPath;
    private string? _sslCaPath;

    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public string BootstrapServers { get => _bootstrapServers; set => this.RaiseAndSetIfChanged(ref _bootstrapServers, value); }
    public AuthType AuthType { get => _authType; set => this.RaiseAndSetIfChanged(ref _authType, value); }
    public string? SaslUsername { get => _saslUsername; set => this.RaiseAndSetIfChanged(ref _saslUsername, value); }
    public string? SaslPassword { get => _saslPassword; set => this.RaiseAndSetIfChanged(ref _saslPassword, value); }
    public string? SslCertPath { get => _sslCertPath; set => this.RaiseAndSetIfChanged(ref _sslCertPath, value); }
    public string? SslKeyPath { get => _sslKeyPath; set => this.RaiseAndSetIfChanged(ref _sslKeyPath, value); }
    public string? SslCaPath { get => _sslCaPath; set => this.RaiseAndSetIfChanged(ref _sslCaPath, value); }

    public bool ShowSaslFields => AuthType is AuthType.SaslPlaintext or AuthType.SaslSsl;
    public bool ShowSslFields => AuthType is AuthType.SaslSsl or AuthType.Ssl;
    public IEnumerable<AuthType> AuthTypes => Enum.GetValues<AuthType>();

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action<ConnectionProfile>? Saved;
    public event Action? Cancelled;

    public ConnectionEditorViewModel()
    {
        this.WhenAnyValue(x => x.AuthType)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(ShowSaslFields));
                this.RaisePropertyChanged(nameof(ShowSslFields));
            });

        SaveCommand = ReactiveCommand.Create(() =>
        {
            var profile = new ConnectionProfile
            {
                Name = Name, BootstrapServers = BootstrapServers, AuthType = AuthType,
                SaslUsername = SaslUsername, SaslPassword = SaslPassword,
                SslCertificatePath = SslCertPath, SslKeyPath = SslKeyPath, SslCaPath = SslCaPath
            };
            Saved?.Invoke(profile);
        });

        CancelCommand = ReactiveCommand.Create(() => Cancelled?.Invoke());
    }
}
