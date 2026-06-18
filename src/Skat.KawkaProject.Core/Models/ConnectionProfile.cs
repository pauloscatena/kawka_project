namespace Skat.KawkaProject.Core.Models;

public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string BootstrapServers { get; set; } = "";
    public AuthType AuthType { get; set; } = AuthType.None;
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
    public string? SslCertificatePath { get; set; }
    public string? SslKeyPath { get; set; }
    public string? SslCaPath { get; set; }
}
