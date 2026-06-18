using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IConnectionProfileRepository
{
    IReadOnlyList<ConnectionProfile> GetAll();
    void Save(ConnectionProfile profile);
    void Delete(string id);
}
