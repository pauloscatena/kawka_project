using Confluent.Kafka;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class KafkaConnectionFactory : IKafkaConnectionFactory
{
    public async Task<IKafkaSession> ConnectAsync(ConnectionProfile profile)
    {
        var session = new KafkaSession(profile);
        var config = new AdminClientConfig();
        session.ApplyTo(config);
        using var admin = new AdminClientBuilder(config).Build();
        await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
        return session;
    }
}
