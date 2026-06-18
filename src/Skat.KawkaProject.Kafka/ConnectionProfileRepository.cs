using System.Text.Json;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class ConnectionProfileRepository : IConnectionProfileRepository
{
    private readonly string _filePath;
    private List<ConnectionProfile> _cache;

    public ConnectionProfileRepository()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KawkaProject")) { }

    public ConnectionProfileRepository(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "profiles.json");
        _cache = Load();
    }

    public IReadOnlyList<ConnectionProfile> GetAll() => _cache.AsReadOnly();

    public void Save(ConnectionProfile profile)
    {
        var idx = _cache.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0) _cache[idx] = profile;
        else _cache.Add(profile);
        Persist();
    }

    public void Delete(string id)
    {
        _cache.RemoveAll(p => p.Id == id);
        Persist();
    }

    private List<ConnectionProfile> Load()
    {
        if (!File.Exists(_filePath)) return new List<ConnectionProfile>();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json)
                   ?? new List<ConnectionProfile>();
        }
        catch { return new List<ConnectionProfile>(); }
    }

    private void Persist() =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_cache));
}
