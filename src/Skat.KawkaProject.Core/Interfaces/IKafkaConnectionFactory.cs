using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IKafkaConnectionFactory
{
    Task<IKafkaSession> ConnectAsync(ConnectionProfile profile);
}
