using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IKafkaSession : IDisposable
{
    string ProfileName { get; }
    string BootstrapServers { get; }
    AuthType AuthType { get; }
    string? SaslUsername { get; }
    string? SaslPassword { get; }
    string? SslCertificatePath { get; }
    string? SslKeyPath { get; }
    string? SslCaPath { get; }
}
