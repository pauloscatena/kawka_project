using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IClusterService
{
    Task<IEnumerable<BrokerInfo>> ListBrokersAsync(IKafkaSession session);
    Task<IEnumerable<ConsumerGroupInfo>> ListConsumerGroupsAsync(IKafkaSession session);
    Task<IEnumerable<PartitionLag>> GetGroupLagAsync(IKafkaSession session, string groupId);
}
