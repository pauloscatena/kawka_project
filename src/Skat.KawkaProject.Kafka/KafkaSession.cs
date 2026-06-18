using Confluent.Kafka;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class KafkaSession : IKafkaSession
{
    private readonly ConnectionProfile _profile;

    public KafkaSession(ConnectionProfile profile) => _profile = profile;

    public string ProfileName => _profile.Name;
    public string BootstrapServers => _profile.BootstrapServers;
    public AuthType AuthType => _profile.AuthType;
    public string? SaslUsername => _profile.SaslUsername;
    public string? SaslPassword => _profile.SaslPassword;
    public string? SslCertificatePath => _profile.SslCertificatePath;
    public string? SslKeyPath => _profile.SslKeyPath;
    public string? SslCaPath => _profile.SslCaPath;

    public void ApplyTo(ClientConfig config)
    {
        config.BootstrapServers = BootstrapServers;
        switch (_profile.AuthType)
        {
            case AuthType.SaslPlaintext:
                config.SecurityProtocol = SecurityProtocol.SaslPlaintext;
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = SaslUsername;
                config.SaslPassword = SaslPassword;
                break;
            case AuthType.SaslSsl:
                config.SecurityProtocol = SecurityProtocol.SaslSsl;
                config.SaslMechanism = SaslMechanism.Plain;
                config.SaslUsername = SaslUsername;
                config.SaslPassword = SaslPassword;
                config.SslCaLocation = SslCaPath;
                break;
            case AuthType.Ssl:
                config.SecurityProtocol = SecurityProtocol.Ssl;
                config.SslCertificateLocation = SslCertificatePath;
                config.SslKeyLocation = SslKeyPath;
                config.SslCaLocation = SslCaPath;
                break;
        }
    }

    public void Dispose() { }
}
