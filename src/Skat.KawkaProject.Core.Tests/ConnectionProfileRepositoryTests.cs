using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;

namespace Skat.KawkaProject.Core.Tests;

public class ConnectionProfileRepositoryTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ConnectionProfileRepository _repo;

    public ConnectionProfileRepositoryTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        _repo = new ConnectionProfileRepository(_tempPath);
    }

    [Fact]
    public void Save_and_GetAll_round_trips_profile()
    {
        var profile = new ConnectionProfile { Name = "Test", BootstrapServers = "localhost:9092" };
        _repo.Save(profile);
        var all = _repo.GetAll();
        Assert.Single(all);
        Assert.Equal("Test", all[0].Name);
    }

    [Fact]
    public void Delete_removes_profile()
    {
        var profile = new ConnectionProfile { Name = "ToDelete", BootstrapServers = "localhost:9092" };
        _repo.Save(profile);
        _repo.Delete(profile.Id);
        Assert.Empty(_repo.GetAll());
    }

    [Fact]
    public void Save_updates_existing_profile_with_same_id()
    {
        var profile = new ConnectionProfile { Name = "Original", BootstrapServers = "localhost:9092" };
        _repo.Save(profile);
        profile.Name = "Updated";
        _repo.Save(profile);
        var all = _repo.GetAll();
        Assert.Single(all);
        Assert.Equal("Updated", all[0].Name);
    }

    [Fact]
    public void GetAll_returns_empty_list_when_no_profiles()
    {
        Assert.Empty(_repo.GetAll());
    }

    public void Dispose() => Directory.Delete(_tempPath, recursive: true);
}
