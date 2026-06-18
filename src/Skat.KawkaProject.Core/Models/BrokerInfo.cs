namespace Skat.KawkaProject.Core.Models;

public record BrokerInfo(int BrokerId, string Host, int Port, bool IsController);
