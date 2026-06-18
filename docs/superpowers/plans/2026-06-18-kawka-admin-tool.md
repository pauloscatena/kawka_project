# Kawka Admin Tool — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform the single-screen Kafka producer into a full desktop admin tool with sidebar navigation, connection profiles, topic management, message browsing, and cluster monitoring.

**Architecture:** Seven compile-time projects — `Core` (interfaces/models), `Kafka` (Confluent.Kafka implementations), four `Features.*` projects (each with ViewModels + Views), and the existing `UI` project as the shell/host. Feature ViewModels depend only on Core interfaces, making them testable without a real broker.

**Tech Stack:** .NET 6, Avalonia 0.10.3, Avalonia.ReactiveUI, Confluent.Kafka 2.3.0, Microsoft.Extensions.DependencyInjection, xUnit, Moq, Testcontainers.Kafka

## Global Constraints

- Target framework: `net6.0` for all projects (upgrade from net5.0)
- Confluent.Kafka: upgrade to `2.3.0`
- Avalonia: keep `0.10.3`
- Namespace root: `Skat.KawkaProject`
- All Kafka calls are async (wrap synchronous Confluent APIs in `Task.Run`)
- No feature ViewModel references `Confluent.Kafka` directly
- Profile storage path: `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)/KawkaProject/profiles.json`
- Passwords stored in plaintext in v1

---

## File Map

**New projects to create:**
```
src/Skat.KawkaProject.Core/
  Models/AuthType.cs
  Models/ConnectionProfile.cs
  Models/TopicInfo.cs
  Models/TopicDetail.cs
  Models/PartitionInfo.cs
  Models/KafkaMessage.cs
  Models/BrokerInfo.cs
  Models/ConsumerGroupInfo.cs
  Models/PartitionLag.cs
  Interfaces/IKafkaSession.cs
  Interfaces/IConnectionProfileRepository.cs
  Interfaces/IKafkaConnectionFactory.cs
  Interfaces/ITopicService.cs
  Interfaces/IMessageService.cs
  Interfaces/IClusterService.cs

src/Skat.KawkaProject.Core.Tests/
  ConnectionProfileRepositoryTests.cs  (unit)

src/Skat.KawkaProject.Kafka/
  KafkaSession.cs
  ConnectionProfileRepository.cs
  KafkaConnectionFactory.cs
  TopicService.cs
  MessageService.cs
  ClusterService.cs

src/Skat.KawkaProject.Kafka.Tests/
  TopicServiceIntegrationTests.cs
  MessageServiceIntegrationTests.cs
  ClusterServiceIntegrationTests.cs

src/Skat.KawkaProject.Features.Connections/
  ViewModels/ConnectionEditorViewModel.cs
  Views/ConnectionEditorView.axaml
  Views/ConnectionEditorView.axaml.cs

src/Skat.KawkaProject.Features.Topics/
  ViewModels/TopicsViewModel.cs
  Views/TopicsView.axaml
  Views/TopicsView.axaml.cs

src/Skat.KawkaProject.Features.Messages/
  ViewModels/MessagesViewModel.cs
  Views/MessagesView.axaml
  Views/MessagesView.axaml.cs

src/Skat.KawkaProject.Features.Cluster/
  ViewModels/ClusterViewModel.cs
  Views/ClusterView.axaml
  Views/ClusterView.axaml.cs

src/Skat.KawkaProject.Features.Tests/
  TopicsViewModelTests.cs
  MessagesViewModelTests.cs
  ClusterViewModelTests.cs
```

**Existing files modified:**
```
src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj   (upgrade TF, add refs)
src/Skat.KawkaProject.UI/Program.cs                     (no change)
src/Skat.KawkaProject.UI/App.axaml.cs                   (add DI setup)
src/Skat.KawkaProject.UI/ViewLocator.cs                 (cross-assembly lookup)
src/Skat.KawkaProject.UI/Views/MainWindow.axaml         (shell grid layout)
src/Skat.KawkaProject.UI/Views/MainWindow.axaml.cs      (strip old click handler)
src/Skat.KawkaProject.UI/ViewModels/ViewModelBase.cs    (no change)
  — DELETE: ViewModels/SendMessageViewModel.cs
  — ADD:    ViewModels/ShellViewModel.cs
  — ADD:    ViewModels/SidebarViewModel.cs
  — ADD:    ViewModels/ConnectionNodeViewModel.cs
  — ADD:    Views/SidebarView.axaml + .axaml.cs
```

---

## Task 1: Create Core project — models and interfaces

**Files:**
- Create: `src/Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj`
- Create: all models and interfaces listed in the file map above

**Interfaces:**
- Produces: `IKafkaSession`, `IConnectionProfileRepository`, `IKafkaConnectionFactory`, `ITopicService`, `IMessageService`, `IClusterService`, all model types

- [ ] **Step 1: Create the Core class library**

```bash
cd /path/to/kawka_project/src
dotnet new classlib -n Skat.KawkaProject.Core -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj
rm Skat.KawkaProject.Core/Class1.cs
mkdir -p Skat.KawkaProject.Core/Models
mkdir -p Skat.KawkaProject.Core/Interfaces
```

- [ ] **Step 2: Write the enum and models**

`src/Skat.KawkaProject.Core/Models/AuthType.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public enum AuthType { None, SaslPlaintext, SaslSsl, Ssl }
```

`src/Skat.KawkaProject.Core/Models/ConnectionProfile.cs`:
```csharp
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
```

`src/Skat.KawkaProject.Core/Models/TopicInfo.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public record TopicInfo(string Name, int PartitionCount, short ReplicationFactor);
```

`src/Skat.KawkaProject.Core/Models/PartitionInfo.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public record PartitionInfo(int PartitionId, int LeaderBrokerId, long EarliestOffset, long LatestOffset);
```

`src/Skat.KawkaProject.Core/Models/TopicDetail.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public record TopicDetail(TopicInfo Topic, IReadOnlyList<PartitionInfo> Partitions);
```

`src/Skat.KawkaProject.Core/Models/KafkaMessage.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public record KafkaMessage(string Topic, int Partition, long Offset, string? Key, string? Value, DateTime Timestamp);
```

`src/Skat.KawkaProject.Core/Models/BrokerInfo.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public record BrokerInfo(int BrokerId, string Host, int Port, bool IsController);
```

`src/Skat.KawkaProject.Core/Models/ConsumerGroupInfo.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public record ConsumerGroupInfo(string GroupId, string State, int MemberCount);
```

`src/Skat.KawkaProject.Core/Models/PartitionLag.cs`:
```csharp
namespace Skat.KawkaProject.Core.Models;

public record PartitionLag(string Topic, int Partition, long CurrentOffset, long EndOffset, long Lag);
```

- [ ] **Step 3: Write the interfaces**

`src/Skat.KawkaProject.Core/Interfaces/IKafkaSession.cs`:
```csharp
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
```

`src/Skat.KawkaProject.Core/Interfaces/IConnectionProfileRepository.cs`:
```csharp
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IConnectionProfileRepository
{
    IReadOnlyList<ConnectionProfile> GetAll();
    void Save(ConnectionProfile profile);
    void Delete(string id);
}
```

`src/Skat.KawkaProject.Core/Interfaces/IKafkaConnectionFactory.cs`:
```csharp
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IKafkaConnectionFactory
{
    Task<IKafkaSession> ConnectAsync(ConnectionProfile profile);
}
```

`src/Skat.KawkaProject.Core/Interfaces/ITopicService.cs`:
```csharp
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface ITopicService
{
    Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session);
    Task<TopicDetail> GetTopicDetailAsync(IKafkaSession session, string topicName);
    Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor);
    Task DeleteTopicAsync(IKafkaSession session, string topicName);
    Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount);
}
```

`src/Skat.KawkaProject.Core/Interfaces/IMessageService.cs`:
```csharp
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IMessageService
{
    Task<IEnumerable<KafkaMessage>> FetchMessagesAsync(
        IKafkaSession session, string topicName, int partition, long startOffset, int count);
    IObservable<KafkaMessage> Tail(IKafkaSession session, string topicName);
}
```

`src/Skat.KawkaProject.Core/Interfaces/IClusterService.cs`:
```csharp
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Interfaces;

public interface IClusterService
{
    Task<IEnumerable<BrokerInfo>> ListBrokersAsync(IKafkaSession session);
    Task<IEnumerable<ConsumerGroupInfo>> ListConsumerGroupsAsync(IKafkaSession session);
    Task<IEnumerable<PartitionLag>> GetGroupLagAsync(IKafkaSession session, string groupId);
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Skat.KawkaProject.Core src/Skat.KawkaProject.sln
git commit -m "feat: add Core project with models and service interfaces"
```

---

## Task 2: Create Kafka project — session, profiles, connection factory

**Files:**
- Create: `src/Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj`
- Create: `src/Skat.KawkaProject.Kafka/KafkaSession.cs`
- Create: `src/Skat.KawkaProject.Kafka/ConnectionProfileRepository.cs`
- Create: `src/Skat.KawkaProject.Kafka/KafkaConnectionFactory.cs`
- Create: `src/Skat.KawkaProject.Core.Tests/` (unit test project)

**Interfaces:**
- Consumes: `IKafkaSession`, `IConnectionProfileRepository`, `IKafkaConnectionFactory`, `ConnectionProfile` from Task 1
- Produces: concrete `KafkaSession`, `ConnectionProfileRepository`, `KafkaConnectionFactory`

- [ ] **Step 1: Create the Kafka project**

```bash
cd src
dotnet new classlib -n Skat.KawkaProject.Kafka -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj
rm Skat.KawkaProject.Kafka/Class1.cs
dotnet add Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj reference Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj
dotnet add Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj package Confluent.Kafka --version 2.3.0
dotnet add Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj package System.Text.Json --version 6.0.0
```

- [ ] **Step 2: Create the unit test project**

```bash
dotnet new xunit -n Skat.KawkaProject.Core.Tests -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Core.Tests/Skat.KawkaProject.Core.Tests.csproj
dotnet add Skat.KawkaProject.Core.Tests/Skat.KawkaProject.Core.Tests.csproj reference Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj
dotnet add Skat.KawkaProject.Core.Tests/Skat.KawkaProject.Core.Tests.csproj package Moq --version 4.20.69
```

- [ ] **Step 3: Write the failing test for ConnectionProfileRepository**

`src/Skat.KawkaProject.Core.Tests/ConnectionProfileRepositoryTests.cs`:
```csharp
using System.IO;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Xunit;

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

    public void Dispose() => Directory.Delete(_tempPath, recursive: true);
}
```

- [ ] **Step 4: Run test to verify it fails**

```bash
dotnet test src/Skat.KawkaProject.Core.Tests/Skat.KawkaProject.Core.Tests.csproj
```
Expected: Build error — `ConnectionProfileRepository` does not exist yet.

- [ ] **Step 5: Implement KafkaSession**

`src/Skat.KawkaProject.Kafka/KafkaSession.cs`:
```csharp
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Confluent.Kafka;

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
```

- [ ] **Step 6: Implement ConnectionProfileRepository**

`src/Skat.KawkaProject.Kafka/ConnectionProfileRepository.cs`:
```csharp
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
```

- [ ] **Step 7: Implement KafkaConnectionFactory**

`src/Skat.KawkaProject.Kafka/KafkaConnectionFactory.cs`:
```csharp
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
```

- [ ] **Step 8: Run tests**

```bash
dotnet test src/Skat.KawkaProject.Core.Tests/Skat.KawkaProject.Core.Tests.csproj
```
Expected: 3 tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/Skat.KawkaProject.Kafka src/Skat.KawkaProject.Core.Tests src/Skat.KawkaProject.sln
git commit -m "feat: add Kafka project with session, profile repository, and connection factory"
```

---

## Task 3: Implement TopicService

**Files:**
- Create: `src/Skat.KawkaProject.Kafka/TopicService.cs`
- Create: `src/Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`

**Interfaces:**
- Consumes: `IKafkaSession` (KafkaSession), `AdminClientBuilder`, `ConsumerBuilder` from Confluent.Kafka
- Produces: `ITopicService` implementation

- [ ] **Step 1: Create the integration test project**

```bash
cd src
dotnet new xunit -n Skat.KawkaProject.Kafka.Tests -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj
dotnet add Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj reference Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj
dotnet add Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj package Testcontainers.Kafka --version 3.9.0
```

- [ ] **Step 2: Write the failing integration test**

`src/Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`:
```csharp
using DotNet.Testcontainers.Builders;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;
using Xunit;

namespace Skat.KawkaProject.Kafka.Tests;

public class TopicServiceIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder().Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    private KafkaSession Session() => new(new ConnectionProfile
    {
        Name = "test",
        BootstrapServers = _kafka.GetBootstrapAddress()
    });

    [Fact]
    public async Task ListTopicsAsync_returns_created_topic()
    {
        var svc = new TopicService();
        using var session = Session();
        await svc.CreateTopicAsync(session, "test-topic", 1, 1);
        var topics = await svc.ListTopicsAsync(session);
        Assert.Contains(topics, t => t.Name == "test-topic");
    }

    [Fact]
    public async Task DeleteTopicAsync_removes_topic()
    {
        var svc = new TopicService();
        using var session = Session();
        await svc.CreateTopicAsync(session, "delete-me", 1, 1);
        await svc.DeleteTopicAsync(session, "delete-me");
        var topics = await svc.ListTopicsAsync(session);
        Assert.DoesNotContain(topics, t => t.Name == "delete-me");
    }

    [Fact]
    public async Task GetTopicDetailAsync_returns_partition_offsets()
    {
        var svc = new TopicService();
        using var session = Session();
        await svc.CreateTopicAsync(session, "detail-topic", 2, 1);
        var detail = await svc.GetTopicDetailAsync(session, "detail-topic");
        Assert.Equal(2, detail.Partitions.Count);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj
```
Expected: Build error — `TopicService` does not exist yet.

- [ ] **Step 4: Implement TopicService**

`src/Skat.KawkaProject.Kafka/TopicService.cs`:
```csharp
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class TopicService : ITopicService
{
    private AdminClientConfig AdminConfig(IKafkaSession session)
    {
        var cfg = new AdminClientConfig();
        ((KafkaSession)session).ApplyTo(cfg);
        return cfg;
    }

    public async Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var meta = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
        return meta.Topics
            .Where(t => !t.Topic.StartsWith("__"))
            .Select(t => new TopicInfo(
                t.Topic,
                t.Partitions.Count,
                (short)t.Partitions[0].Replicas.Length));
    }

    public async Task<TopicDetail> GetTopicDetailAsync(IKafkaSession session, string topicName)
    {
        var adminCfg = AdminConfig(session);
        using var admin = new AdminClientBuilder(adminCfg).Build();
        var meta = await Task.Run(() => admin.GetMetadata(topicName, TimeSpan.FromSeconds(10)));
        var topicMeta = meta.Topics.First();

        var consumerCfg = new ConsumerConfig { GroupId = $"kawka-detail-{Guid.NewGuid()}" };
        ((KafkaSession)session).ApplyTo(consumerCfg);
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(consumerCfg).Build();

        var partitions = topicMeta.Partitions.Select(p =>
        {
            var tp = new TopicPartition(topicName, new Partition(p.PartitionId));
            var wm = consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5));
            return new PartitionInfo(p.PartitionId, p.Leader, wm.Low.Value, wm.High.Value);
        }).ToList();

        var info = new TopicInfo(topicMeta.Topic, partitions.Count, (short)topicMeta.Partitions[0].Replicas.Length);
        return new TopicDetail(info, partitions);
    }

    public async Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification { Name = name, NumPartitions = partitionCount, ReplicationFactor = replicationFactor }
        });
    }

    public async Task DeleteTopicAsync(IKafkaSession session, string topicName)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.DeleteTopicsAsync(new[] { topicName });
    }

    public async Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.CreatePartitionsAsync(new[]
        {
            new PartitionsSpecification { Topic = topicName, IncreaseTo = newPartitionCount }
        });
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj --filter "TopicService"
```
Expected: 3 tests pass. (Docker must be running for Testcontainers.)

- [ ] **Step 6: Commit**

```bash
git add src/Skat.KawkaProject.Kafka/TopicService.cs src/Skat.KawkaProject.Kafka.Tests
git commit -m "feat: implement TopicService with integration tests"
```

---

## Task 4: Implement MessageService

**Files:**
- Create: `src/Skat.KawkaProject.Kafka/MessageService.cs`
- Modify: `src/Skat.KawkaProject.Kafka.Tests/` (add MessageServiceIntegrationTests.cs)

**Interfaces:**
- Consumes: `IKafkaSession` (KafkaSession)
- Produces: `IMessageService` implementation

- [ ] **Step 1: Add package dependency**

```bash
dotnet add src/Skat.KawkaProject.Kafka/Skat.KawkaProject.Kafka.csproj package System.Reactive --version 6.0.1
```

- [ ] **Step 2: Write the failing test**

`src/Skat.KawkaProject.Kafka.Tests/MessageServiceIntegrationTests.cs`:
```csharp
using Confluent.Kafka;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;
using Xunit;

namespace Skat.KawkaProject.Kafka.Tests;

public class MessageServiceIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder().Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    private KafkaSession Session() => new(new ConnectionProfile
    {
        Name = "test", BootstrapServers = _kafka.GetBootstrapAddress()
    });

    private async Task ProduceAsync(string topic, string value)
    {
        var cfg = new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using var producer = new ProducerBuilder<Null, string>(cfg).Build();
        await producer.ProduceAsync(topic, new Message<Null, string> { Value = value });
        producer.Flush(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FetchMessagesAsync_returns_produced_messages()
    {
        var topic = "fetch-test";
        await ProduceAsync(topic, "hello");
        await ProduceAsync(topic, "world");

        var svc = new MessageService();
        using var session = Session();
        var messages = await svc.FetchMessagesAsync(session, topic, 0, 0, 10);

        Assert.Equal(2, messages.Count());
        Assert.Equal("hello", messages.First().Value);
    }

    [Fact]
    public async Task Tail_receives_messages_as_observable()
    {
        var topic = "tail-test";
        var svc = new MessageService();
        using var session = Session();

        var received = new List<KafkaMessage>();
        using var sub = svc.Tail(session, topic).Subscribe(m => received.Add(m));

        await Task.Delay(500);
        await ProduceAsync(topic, "live-message");
        await Task.Delay(2000);

        Assert.Single(received);
        Assert.Equal("live-message", received[0].Value);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

```bash
dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj --filter "MessageService"
```
Expected: Build error — `MessageService` does not exist.

- [ ] **Step 4: Implement MessageService**

`src/Skat.KawkaProject.Kafka/MessageService.cs`:
```csharp
using System.Reactive.Linq;
using Confluent.Kafka;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class MessageService : IMessageService
{
    public async Task<IEnumerable<KafkaMessage>> FetchMessagesAsync(
        IKafkaSession session, string topicName, int partition, long startOffset, int count)
    {
        var cfg = new ConsumerConfig
        {
            GroupId = $"kawka-fetch-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        ((KafkaSession)session).ApplyTo(cfg);

        var messages = new List<KafkaMessage>();
        using var consumer = new ConsumerBuilder<string, string>(cfg).Build();
        consumer.Assign(new TopicPartitionOffset(topicName, partition, startOffset));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (messages.Count < count)
            {
                var result = consumer.Consume(cts.Token);
                if (result?.Message == null) break;
                messages.Add(ToMessage(result));
            }
        }
        catch (OperationCanceledException) { }

        return messages;
    }

    public IObservable<KafkaMessage> Tail(IKafkaSession session, string topicName) =>
        Observable.Create<KafkaMessage>(observer =>
        {
            var cfg = new ConsumerConfig
            {
                GroupId = $"kawka-tail-{Guid.NewGuid()}",
                AutoOffsetReset = AutoOffsetReset.Latest,
                EnableAutoCommit = false
            };
            ((KafkaSession)session).ApplyTo(cfg);

            var consumer = new ConsumerBuilder<string, string>(cfg).Build();
            consumer.Subscribe(topicName);
            var cts = new CancellationTokenSource();

            Task.Run(() =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var result = consumer.Consume(cts.Token);
                        if (result?.Message != null)
                            observer.OnNext(ToMessage(result));
                    }
                    observer.OnCompleted();
                }
                catch (OperationCanceledException) { observer.OnCompleted(); }
                catch (Exception ex) { observer.OnError(ex); }
                finally { consumer.Dispose(); }
            }, cts.Token);

            return () => cts.Cancel();
        });

    private static KafkaMessage ToMessage(ConsumeResult<string, string> r) =>
        new(r.Topic, r.Partition.Value, r.Offset.Value,
            r.Message.Key, r.Message.Value,
            r.Message.Timestamp.UtcDateTime);
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj --filter "MessageService"
```
Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Skat.KawkaProject.Kafka/MessageService.cs src/Skat.KawkaProject.Kafka.Tests/MessageServiceIntegrationTests.cs
git commit -m "feat: implement MessageService with fetch and tail observable"
```

---

## Task 5: Implement ClusterService

**Files:**
- Create: `src/Skat.KawkaProject.Kafka/ClusterService.cs`
- Modify: `src/Skat.KawkaProject.Kafka.Tests/` (add ClusterServiceIntegrationTests.cs)

- [ ] **Step 1: Write the failing test**

`src/Skat.KawkaProject.Kafka.Tests/ClusterServiceIntegrationTests.cs`:
```csharp
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;
using Xunit;

namespace Skat.KawkaProject.Kafka.Tests;

public class ClusterServiceIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder().Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    private KafkaSession Session() => new(new ConnectionProfile
    {
        Name = "test", BootstrapServers = _kafka.GetBootstrapAddress()
    });

    [Fact]
    public async Task ListBrokersAsync_returns_at_least_one_broker()
    {
        var svc = new ClusterService();
        using var session = Session();
        var brokers = await svc.ListBrokersAsync(session);
        Assert.NotEmpty(brokers);
    }

    [Fact]
    public async Task ListConsumerGroupsAsync_returns_created_group()
    {
        // Produce + consume to create a group
        var bootstrap = _kafka.GetBootstrapAddress();
        var producerCfg = new ProducerConfig { BootstrapServers = bootstrap };
        using var producer = new ProducerBuilder<Null, string>(producerCfg).Build();
        await producer.ProduceAsync("grp-topic", new Message<Null, string> { Value = "x" });
        producer.Flush(TimeSpan.FromSeconds(3));

        var consumerCfg = new ConsumerConfig
        {
            BootstrapServers = bootstrap, GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = true
        };
        using var consumer = new ConsumerBuilder<Null, string>(consumerCfg).Build();
        consumer.Subscribe("grp-topic");
        consumer.Consume(TimeSpan.FromSeconds(5));
        consumer.Close();

        var svc = new ClusterService();
        using var session = Session();
        var groups = await svc.ListConsumerGroupsAsync(session);
        Assert.Contains(groups, g => g.GroupId == "test-group");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj --filter "ClusterService"
```
Expected: Build error — `ClusterService` does not exist.

- [ ] **Step 3: Implement ClusterService**

`src/Skat.KawkaProject.Kafka/ClusterService.cs`:
```csharp
using Confluent.Kafka;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Kafka;

public class ClusterService : IClusterService
{
    private AdminClientConfig AdminConfig(IKafkaSession session)
    {
        var cfg = new AdminClientConfig();
        ((KafkaSession)session).ApplyTo(cfg);
        return cfg;
    }

    public async Task<IEnumerable<BrokerInfo>> ListBrokersAsync(IKafkaSession session)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var meta = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
        return meta.Brokers.Select(b => new BrokerInfo(
            b.BrokerId, b.Host, b.Port,
            b.BrokerId == meta.OriginatingBrokerId));
    }

    public async Task<IEnumerable<ConsumerGroupInfo>> ListConsumerGroupsAsync(IKafkaSession session)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var result = await admin.ListConsumerGroupsAsync();
        return result.Valid.Select(g => new ConsumerGroupInfo(g.GroupId, g.State.ToString(), 0));
    }

    public async Task<IEnumerable<PartitionLag>> GetGroupLagAsync(IKafkaSession session, string groupId)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();

        // Confluent.Kafka 2.x: get committed offsets per partition for the group
        var offsetsResult = await admin.ListConsumerGroupOffsetsAsync(
            new[] { new ConsumerGroupTopicPartitions(groupId) });
        var partitionOffsets = offsetsResult[0].Partitions;

        if (!partitionOffsets.Any()) return Enumerable.Empty<PartitionLag>();

        var consumerCfg = new ConsumerConfig { GroupId = $"kawka-lag-{Guid.NewGuid()}" };
        ((KafkaSession)session).ApplyTo(consumerCfg);
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(consumerCfg).Build();

        return partitionOffsets.Select(po =>
        {
            var wm = consumer.QueryWatermarkOffsets(
                new TopicPartition(po.Topic, po.Partition), TimeSpan.FromSeconds(5));
            var current = po.Offset.IsSpecial ? 0 : po.Offset.Value;
            return new PartitionLag(po.Topic, po.Partition.Value, current, wm.High.Value,
                wm.High.Value - current);
        });
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj --filter "ClusterService"
```
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Skat.KawkaProject.Kafka/ClusterService.cs src/Skat.KawkaProject.Kafka.Tests/ClusterServiceIntegrationTests.cs
git commit -m "feat: implement ClusterService with broker and consumer group support"
```

---

## Task 6: Restructure UI project — shell layout and DI

**Files:**
- Modify: `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`
- Modify: `src/Skat.KawkaProject.UI/App.axaml.cs`
- Modify: `src/Skat.KawkaProject.UI/ViewLocator.cs`
- Modify: `src/Skat.KawkaProject.UI/Views/MainWindow.axaml`
- Modify: `src/Skat.KawkaProject.UI/Views/MainWindow.axaml.cs`
- Delete: `src/Skat.KawkaProject.UI/ViewModels/SendMessageViewModel.cs`
- Create: `src/Skat.KawkaProject.UI/ViewModels/ShellViewModel.cs`
- Create: `src/Skat.KawkaProject.UI/ViewModels/SidebarViewModel.cs`
- Create: `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`
- Create: `src/Skat.KawkaProject.UI/Views/SidebarView.axaml` + `.axaml.cs`

**Interfaces:**
- Consumes: all Core interfaces, all Kafka implementations, all feature projects (added in later tasks)
- Produces: working shell with sidebar + empty content area

- [ ] **Step 1: Update csproj**

Replace `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net6.0</TargetFramework>
        <Nullable>enable</Nullable>
    </PropertyGroup>
    <ItemGroup>
        <AvaloniaResource Include="Assets\**" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="Avalonia" Version="0.10.3" />
        <PackageReference Include="Avalonia.Desktop" Version="0.10.3" />
        <PackageReference Include="Avalonia.Diagnostics" Version="0.10.3" />
        <PackageReference Include="Avalonia.ReactiveUI" Version="0.10.3" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="6.0.0" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="..\Skat.KawkaProject.Kafka\Skat.KawkaProject.Kafka.csproj" />
    </ItemGroup>
</Project>
```

*(Feature project references will be added in Tasks 7–10 as each is created.)*

- [ ] **Step 2: Delete the old ViewModel and code-behind logic**

```bash
rm src/Skat.KawkaProject.UI/ViewModels/SendMessageViewModel.cs
```

Strip `MainWindow.axaml.cs` down to just initialization:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Skat.KawkaProject.UI.ViewModels;

namespace Skat.KawkaProject.UI.Views;

public partial class MainWindow : ReactiveWindow<ShellViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 3: Create ShellViewModel**

`src/Skat.KawkaProject.UI/ViewModels/ShellViewModel.cs`:
```csharp
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.UI.ViewModels;

public class ShellViewModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new();
    public SidebarViewModel Sidebar { get; }

    public ShellViewModel(
        IConnectionProfileRepository profileRepo,
        IKafkaConnectionFactory connectionFactory,
        ITopicService topicService,
        IMessageService messageService,
        IClusterService clusterService)
    {
        Sidebar = new SidebarViewModel(this, profileRepo, connectionFactory,
            topicService, messageService, clusterService);
    }
}
```

- [ ] **Step 4: Create ConnectionNodeViewModel**

`src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`:
```csharp
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.UI.ViewModels;

public enum ConnectionStatus { Disconnected, Connecting, Connected, Error }

public class ConnectionNodeViewModel : ReactiveObject
{
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private string? _errorMessage;
    private IKafkaSession? _session;

    public ConnectionProfile Profile { get; }
    public string Name => Profile.Name;

    public ConnectionStatus Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand NavigateToTopicsCommand { get; }
    public ICommand NavigateToMessagesCommand { get; }
    public ICommand NavigateToClusterCommand { get; }
    public ICommand DeleteCommand { get; }

    public ConnectionNodeViewModel(
        ConnectionProfile profile,
        IScreen shell,
        IKafkaConnectionFactory factory,
        ITopicService topicService,
        IMessageService messageService,
        IClusterService clusterService,
        Action<ConnectionNodeViewModel> onDelete)
    {
        Profile = profile;

        ConnectCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            Status = ConnectionStatus.Connecting;
            ErrorMessage = null;
            try
            {
                _session = await factory.ConnectAsync(Profile);
                Status = ConnectionStatus.Connected;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Status = ConnectionStatus.Error;
            }
        });

        DisconnectCommand = ReactiveCommand.Create(() =>
        {
            _session?.Dispose();
            _session = null;
            Status = ConnectionStatus.Disconnected;
        });

        NavigateToTopicsCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            // Populated in Task 8 when Features.Topics is added
        });

        NavigateToMessagesCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            // Populated in Task 9 when Features.Messages is added
        });

        NavigateToClusterCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            // Populated in Task 10 when Features.Cluster is added
        });

        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
    }
}
```

- [ ] **Step 5: Create SidebarViewModel**

`src/Skat.KawkaProject.UI/ViewModels/SidebarViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.UI.ViewModels;

public class SidebarViewModel : ReactiveObject
{
    private readonly IScreen _shell;
    private readonly IConnectionProfileRepository _profileRepo;
    private readonly IKafkaConnectionFactory _connectionFactory;
    private readonly ITopicService _topicService;
    private readonly IMessageService _messageService;
    private readonly IClusterService _clusterService;

    public ObservableCollection<ConnectionNodeViewModel> Connections { get; } = new();
    public ICommand AddConnectionCommand { get; }

    public SidebarViewModel(
        IScreen shell,
        IConnectionProfileRepository profileRepo,
        IKafkaConnectionFactory connectionFactory,
        ITopicService topicService,
        IMessageService messageService,
        IClusterService clusterService)
    {
        _shell = shell;
        _profileRepo = profileRepo;
        _connectionFactory = connectionFactory;
        _topicService = topicService;
        _messageService = messageService;
        _clusterService = clusterService;

        foreach (var profile in profileRepo.GetAll())
            Connections.Add(CreateNode(profile));

        AddConnectionCommand = ReactiveCommand.Create(OpenAddConnectionDialog);
    }

    private void OpenAddConnectionDialog()
    {
        // Wired in Task 7 when Features.Connections is added
    }

    internal void AddProfile(ConnectionProfile profile)
    {
        _profileRepo.Save(profile);
        Connections.Add(CreateNode(profile));
    }

    private ConnectionNodeViewModel CreateNode(ConnectionProfile profile) =>
        new(profile, _shell, _connectionFactory, _topicService, _messageService,
            _clusterService, node =>
            {
                _profileRepo.Delete(node.Profile.Id);
                Connections.Remove(node);
            });
}
```

- [ ] **Step 6: Update ViewLocator for cross-assembly lookup**

Replace the body of `ViewLocator.cs`:
```csharp
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ReactiveUI;

namespace Skat.KawkaProject.UI;

public class ViewLocator : IDataTemplate
{
    public bool SupportsRecycling => false;

    public IControl Build(object data)
    {
        var viewName = data.GetType().FullName!
            .Replace("ViewModels.", "Views.")
            .Replace("ViewModel", "View");

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.FullName == viewName);

        return type != null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "View not found: " + viewName };
    }

    public bool Match(object data) => data is ReactiveObject;
}
```

- [ ] **Step 7: Redesign MainWindow.axaml**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Skat.KawkaProject.UI.ViewModels"
        xmlns:rxui="clr-namespace:Avalonia.ReactiveUI;assembly=Avalonia.ReactiveUI"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        x:Class="Skat.KawkaProject.UI.Views.MainWindow"
        Width="1100" Height="700"
        Title="Kawka — Kafka Admin">

    <Design.DataContext>
        <vm:ShellViewModel />
    </Design.DataContext>

    <Grid ColumnDefinitions="260,*">
        <Border Grid.Column="0" BorderBrush="#333333" BorderThickness="0,0,1,0">
            <ContentControl Content="{Binding Sidebar}" />
        </Border>
        <rxui:RoutedViewHost Grid.Column="1" Router="{Binding Router}" />
    </Grid>
</Window>
```

- [ ] **Step 8: Create SidebarView.axaml**

`src/Skat.KawkaProject.UI/Views/SidebarView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Skat.KawkaProject.UI.ViewModels"
             x:Class="Skat.KawkaProject.UI.Views.SidebarView">
    <DockPanel>
        <Button DockPanel.Dock="Top" Command="{Binding AddConnectionCommand}"
                HorizontalAlignment="Stretch" Margin="8">
            + Add Connection
        </Button>
        <Separator DockPanel.Dock="Top" />
        <ScrollViewer>
            <ItemsControl Items="{Binding Connections}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:ConnectionNodeViewModel}">
                        <Expander Margin="4,2">
                            <Expander.Header>
                                <StackPanel Orientation="Horizontal" Spacing="6">
                                    <Ellipse Width="8" Height="8">
                                        <Ellipse.Fill>
                                            <MultiBinding>
                                                <!-- Status dot color handled via converter in code-behind -->
                                            </MultiBinding>
                                        </Ellipse.Fill>
                                    </Ellipse>
                                    <TextBlock Text="{Binding Name}" VerticalAlignment="Center" />
                                </StackPanel>
                            </Expander.Header>
                            <StackPanel Margin="16,4,0,4" Spacing="2">
                                <Button Command="{Binding ConnectCommand}">Connect</Button>
                                <Button Command="{Binding NavigateToTopicsCommand}">Topics</Button>
                                <Button Command="{Binding NavigateToMessagesCommand}">Messages</Button>
                                <Button Command="{Binding NavigateToClusterCommand}">Cluster Info</Button>
                                <Separator />
                                <Button Command="{Binding DeleteCommand}" Foreground="Red">Remove</Button>
                            </StackPanel>
                        </Expander>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/Skat.KawkaProject.UI/Views/SidebarView.axaml.cs`:
```csharp
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Skat.KawkaProject.UI.ViewModels;

namespace Skat.KawkaProject.UI.Views;

public partial class SidebarView : ReactiveUserControl<SidebarViewModel>
{
    public SidebarView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 9: Update App.axaml.cs with DI**

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Kafka;
using Skat.KawkaProject.UI.ViewModels;
using Skat.KawkaProject.UI.Views;

namespace Skat.KawkaProject.UI;

public class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IConnectionProfileRepository, ConnectionProfileRepository>();
        collection.AddSingleton<IKafkaConnectionFactory, KafkaConnectionFactory>();
        collection.AddTransient<ITopicService, TopicService>();
        collection.AddTransient<IMessageService, MessageService>();
        collection.AddTransient<IClusterService, ClusterService>();
        collection.AddSingleton<ShellViewModel>();
        Services = collection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<ShellViewModel>()
            };

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 10: Build the UI project**

```bash
dotnet build src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj
```
Expected: Build succeeded, 0 errors. (App will run but content area is empty — that's expected until Tasks 7–10.)

- [ ] **Step 11: Commit**

```bash
git add src/Skat.KawkaProject.UI
git commit -m "feat: restructure UI project with shell layout, DI, sidebar navigation"
```

---

## Task 7: Features.Connections — connection editor dialog

**Files:**
- Create: `src/Skat.KawkaProject.Features.Connections/Skat.KawkaProject.Features.Connections.csproj`
- Create: `src/Skat.KawkaProject.Features.Connections/ViewModels/ConnectionEditorViewModel.cs`
- Create: `src/Skat.KawkaProject.Features.Connections/Views/ConnectionEditorView.axaml` + `.axaml.cs`
- Modify: `src/Skat.KawkaProject.UI/ViewModels/SidebarViewModel.cs` (wire up dialog)
- Modify: `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj` (add project reference)

**Interfaces:**
- Consumes: `ConnectionProfile`, `IConnectionProfileRepository`, `AuthType` from Core
- Produces: `ConnectionEditorViewModel` that emits a saved profile, callable from SidebarViewModel

- [ ] **Step 1: Create the project**

```bash
cd src
dotnet new classlib -n Skat.KawkaProject.Features.Connections -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Features.Connections/Skat.KawkaProject.Features.Connections.csproj
rm Skat.KawkaProject.Features.Connections/Class1.cs
dotnet add Skat.KawkaProject.Features.Connections/Skat.KawkaProject.Features.Connections.csproj reference Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj
dotnet add Skat.KawkaProject.Features.Connections/Skat.KawkaProject.Features.Connections.csproj package Avalonia --version 0.10.3
dotnet add Skat.KawkaProject.Features.Connections/Skat.KawkaProject.Features.Connections.csproj package Avalonia.ReactiveUI --version 0.10.3
dotnet add Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj reference Skat.KawkaProject.Features.Connections/Skat.KawkaProject.Features.Connections.csproj
mkdir -p Skat.KawkaProject.Features.Connections/ViewModels
mkdir -p Skat.KawkaProject.Features.Connections/Views
```

- [ ] **Step 2: Implement ConnectionEditorViewModel**

`src/Skat.KawkaProject.Features.Connections/ViewModels/ConnectionEditorViewModel.cs`:
```csharp
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Connections.ViewModels;

public class ConnectionEditorViewModel : ReactiveObject
{
    private string _name = "";
    private string _bootstrapServers = "";
    private AuthType _authType = AuthType.None;
    private string? _saslUsername;
    private string? _saslPassword;
    private string? _sslCertPath;
    private string? _sslKeyPath;
    private string? _sslCaPath;

    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public string BootstrapServers { get => _bootstrapServers; set => this.RaiseAndSetIfChanged(ref _bootstrapServers, value); }
    public AuthType AuthType { get => _authType; set => this.RaiseAndSetIfChanged(ref _authType, value); }
    public string? SaslUsername { get => _saslUsername; set => this.RaiseAndSetIfChanged(ref _saslUsername, value); }
    public string? SaslPassword { get => _saslPassword; set => this.RaiseAndSetIfChanged(ref _saslPassword, value); }
    public string? SslCertPath { get => _sslCertPath; set => this.RaiseAndSetIfChanged(ref _sslCertPath, value); }
    public string? SslKeyPath { get => _sslKeyPath; set => this.RaiseAndSetIfChanged(ref _sslKeyPath, value); }
    public string? SslCaPath { get => _sslCaPath; set => this.RaiseAndSetIfChanged(ref _sslCaPath, value); }

    public bool ShowSaslFields => AuthType is AuthType.SaslPlaintext or AuthType.SaslSsl;
    public bool ShowSslFields => AuthType is AuthType.SaslSsl or AuthType.Ssl;
    public IEnumerable<AuthType> AuthTypes => Enum.GetValues<AuthType>();

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action<ConnectionProfile>? Saved;
    public event Action? Cancelled;

    public ConnectionEditorViewModel()
    {
        this.WhenAnyValue(x => x.AuthType)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(ShowSaslFields));
                this.RaisePropertyChanged(nameof(ShowSslFields));
            });

        SaveCommand = ReactiveCommand.Create(() =>
        {
            var profile = new ConnectionProfile
            {
                Name = Name, BootstrapServers = BootstrapServers, AuthType = AuthType,
                SaslUsername = SaslUsername, SaslPassword = SaslPassword,
                SslCertificatePath = SslCertPath, SslKeyPath = SslKeyPath, SslCaPath = SslCaPath
            };
            Saved?.Invoke(profile);
        });

        CancelCommand = ReactiveCommand.Create(() => Cancelled?.Invoke());
    }
}
```

- [ ] **Step 3: Create ConnectionEditorView**

`src/Skat.KawkaProject.Features.Connections/Views/ConnectionEditorView.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Skat.KawkaProject.Features.Connections.ViewModels"
        x:Class="Skat.KawkaProject.Features.Connections.Views.ConnectionEditorView"
        Title="Add / Edit Connection" Width="480" Height="400"
        CanResize="False">
    <StackPanel Margin="16" Spacing="8">
        <TextBlock>Name:</TextBlock>
        <TextBox Text="{Binding Name}" />

        <TextBlock>Bootstrap Servers (e.g. localhost:9092):</TextBlock>
        <TextBox Text="{Binding BootstrapServers}" />

        <TextBlock>Authentication:</TextBlock>
        <ComboBox Items="{Binding AuthTypes}" SelectedItem="{Binding AuthType}" />

        <StackPanel IsVisible="{Binding ShowSaslFields}" Spacing="4">
            <TextBlock>SASL Username:</TextBlock>
            <TextBox Text="{Binding SaslUsername}" />
            <TextBlock>SASL Password:</TextBlock>
            <TextBox Text="{Binding SaslPassword}" PasswordChar="*" />
        </StackPanel>

        <StackPanel IsVisible="{Binding ShowSslFields}" Spacing="4">
            <TextBlock>SSL Certificate Path:</TextBlock>
            <TextBox Text="{Binding SslCertPath}" />
            <TextBlock>SSL Key Path:</TextBlock>
            <TextBox Text="{Binding SslKeyPath}" />
            <TextBlock>SSL CA Path:</TextBlock>
            <TextBox Text="{Binding SslCaPath}" />
        </StackPanel>

        <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
            <Button Command="{Binding CancelCommand}">Cancel</Button>
            <Button Command="{Binding SaveCommand}">Save</Button>
        </StackPanel>
    </StackPanel>
</Window>
```

`src/Skat.KawkaProject.Features.Connections/Views/ConnectionEditorView.axaml.cs`:
```csharp
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Connections.ViewModels;

namespace Skat.KawkaProject.Features.Connections.Views;

public partial class ConnectionEditorView : ReactiveWindow<ConnectionEditorViewModel>
{
    public ConnectionEditorView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 4: Wire the dialog into SidebarViewModel**

In `SidebarViewModel.cs`, replace `OpenAddConnectionDialog`:
```csharp
private void OpenAddConnectionDialog()
{
    var vm = new ConnectionEditorViewModel();
    var dialog = new Skat.KawkaProject.Features.Connections.Views.ConnectionEditorView
    {
        DataContext = vm
    };
    vm.Saved += profile =>
    {
        AddProfile(profile);
        dialog.Close();
    };
    vm.Cancelled += () => dialog.Close();
    dialog.Show();
}
```

- [ ] **Step 5: Build**

```bash
dotnet build src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Skat.KawkaProject.Features.Connections src/Skat.KawkaProject.UI
git commit -m "feat: add connection editor dialog with auth type support"
```

---

## Task 8: Features.Topics

**Files:**
- Create: `src/Skat.KawkaProject.Features.Topics/Skat.KawkaProject.Features.Topics.csproj`
- Create: `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Create: `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml` + `.axaml.cs`
- Create: `src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`
- Modify: `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs` (wire NavigateToTopicsCommand)
- Modify: `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj` (add project reference)

**Interfaces:**
- Consumes: `ITopicService`, `IKafkaSession`, `TopicInfo`, `TopicDetail` from Core
- Produces: `TopicsViewModel` — navigable via ReactiveUI router

- [ ] **Step 1: Create project and test project**

```bash
cd src
dotnet new classlib -n Skat.KawkaProject.Features.Topics -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Features.Topics/Skat.KawkaProject.Features.Topics.csproj
rm Skat.KawkaProject.Features.Topics/Class1.cs
dotnet add Skat.KawkaProject.Features.Topics/Skat.KawkaProject.Features.Topics.csproj reference Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj
dotnet add Skat.KawkaProject.Features.Topics/Skat.KawkaProject.Features.Topics.csproj package Avalonia --version 0.10.3
dotnet add Skat.KawkaProject.Features.Topics/Skat.KawkaProject.Features.Topics.csproj package Avalonia.ReactiveUI --version 0.10.3
dotnet add Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj reference Skat.KawkaProject.Features.Topics/Skat.KawkaProject.Features.Topics.csproj
mkdir -p Skat.KawkaProject.Features.Topics/ViewModels
mkdir -p Skat.KawkaProject.Features.Topics/Views

dotnet new xunit -n Skat.KawkaProject.Features.Tests -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj
dotnet add Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj reference Skat.KawkaProject.Features.Topics/Skat.KawkaProject.Features.Topics.csproj
dotnet add Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj package Moq --version 4.20.69
dotnet add Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj package ReactiveUI --version 18.4.30
```

- [ ] **Step 2: Write failing ViewModel tests**

`src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Topics.ViewModels;
using Xunit;

namespace Skat.KawkaProject.Features.Tests;

public class TopicsViewModelTests
{
    private static IScreen FakeScreen()
    {
        var mock = new Mock<IScreen>();
        mock.Setup(s => s.Router).Returns(new RoutingState());
        return mock.Object;
    }

    private static IKafkaSession FakeSession()
    {
        var mock = new Mock<IKafkaSession>();
        mock.Setup(s => s.ProfileName).Returns("test");
        return mock.Object;
    }

    [Fact]
    public async Task LoadTopicsAsync_populates_Topics_collection()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 3, 1) });

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object);
        await vm.LoadTopicsAsync();

        Assert.Single(vm.Topics);
        Assert.Equal("orders", vm.Topics[0].Name);
    }

    [Fact]
    public async Task DeleteTopic_removes_from_Topics_collection()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("to-delete", 1, 1) });
        svc.Setup(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "to-delete"))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object);
        await vm.LoadTopicsAsync();
        await vm.DeleteTopicAsync("to-delete");

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "to-delete"), Times.Once);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

```bash
dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "TopicsViewModel"
```
Expected: Build error — `TopicsViewModel` does not exist.

- [ ] **Step 4: Implement TopicsViewModel**

`src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Topics.ViewModels;

public class TopicsViewModel : ReactiveObject, IRoutableViewModel
{
    private readonly IKafkaSession _session;
    private readonly ITopicService _topicService;
    private bool _isBusy;
    private string? _errorMessage;
    private TopicInfo? _selectedTopic;
    private TopicDetail? _selectedTopicDetail;
    private string _filter = "";

    public IScreen HostScreen { get; }
    public string UrlPathSegment => "topics";

    public ObservableCollection<TopicInfo> Topics { get; } = new();
    public ICommand LoadCommand { get; }
    public ICommand CreateTopicCommand { get; }
    public ICommand DeleteTopicCommand { get; }
    public ICommand DismissErrorCommand { get; }

    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public string? ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }

    public string Filter
    {
        get => _filter;
        set
        {
            this.RaiseAndSetIfChanged(ref _filter, value);
            ApplyFilter();
        }
    }

    public TopicInfo? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTopic, value);
            if (value != null) _ = LoadDetailAsync(value.Name);
        }
    }

    public TopicDetail? SelectedTopicDetail
    {
        get => _selectedTopicDetail;
        private set => this.RaiseAndSetIfChanged(ref _selectedTopicDetail, value);
    }

    private List<TopicInfo> _allTopics = new();

    public TopicsViewModel(IScreen hostScreen, IKafkaSession session, ITopicService topicService)
    {
        HostScreen = hostScreen;
        _session = session;
        _topicService = topicService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadTopicsAsync);
        DeleteTopicCommand = ReactiveCommand.CreateFromTask<string>(DeleteTopicAsync);
        CreateTopicCommand = ReactiveCommand.CreateFromTask<(string name, int partitions, short replication)>(
            async args => await CreateTopicAsync(args.name, args.partitions, args.replication));
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);

        _ = LoadTopicsAsync();
    }

    public async Task LoadTopicsAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _allTopics = (await _topicService.ListTopicsAsync(_session)).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task DeleteTopicAsync(string topicName)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.DeleteTopicAsync(_session, topicName);
            _allTopics.RemoveAll(t => t.Name == topicName);
            ApplyFilter();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task CreateTopicAsync(string name, int partitions, short replication)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.CreateTopicAsync(_session, name, partitions, replication);
            await LoadTopicsAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task LoadDetailAsync(string topicName)
    {
        try { SelectedTopicDetail = await _topicService.GetTopicDetailAsync(_session, topicName); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private void ApplyFilter()
    {
        Topics.Clear();
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? _allTopics
            : _allTopics.Where(t => t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        foreach (var t in filtered) Topics.Add(t);
    }
}
```

- [ ] **Step 5: Create TopicsView.axaml**

`src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Skat.KawkaProject.Features.Topics.Views.TopicsView">
    <DockPanel>
        <!-- Toolbar -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="8">
            <TextBox Text="{Binding Filter}" Watermark="Filter topics..." Width="200" />
            <Button Command="{Binding LoadCommand}">Refresh</Button>
            <Button Name="CreateBtn">Create Topic</Button>
        </StackPanel>

        <!-- Error banner (dismissible) -->
        <Border DockPanel.Dock="Top" Background="#FFDDDD" Padding="8"
                IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Text="{Binding ErrorMessage}" Foreground="DarkRed" TextWrapping="Wrap" />
                <Button Grid.Column="1" Command="{Binding DismissErrorCommand}" Padding="4,0">✕</Button>
            </Grid>
        </Border>

        <!-- Loading indicator -->
        <ProgressBar DockPanel.Dock="Top" IsIndeterminate="True"
                     IsVisible="{Binding IsBusy}" Height="4" />

        <!-- Topic list + detail panel -->
        <Grid ColumnDefinitions="*,300">
            <ListBox Grid.Column="0" Items="{Binding Topics}"
                     SelectedItem="{Binding SelectedTopic}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="*,Auto,Auto">
                            <TextBlock Grid.Column="0" Text="{Binding Name}" />
                            <TextBlock Grid.Column="1" Text="{Binding PartitionCount}" Margin="8,0" />
                            <TextBlock Grid.Column="2" Text="{Binding ReplicationFactor}" />
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Detail panel -->
            <Border Grid.Column="1" Padding="8"
                    IsVisible="{Binding SelectedTopicDetail, Converter={x:Static ObjectConverters.IsNotNull}}">
                <StackPanel>
                    <TextBlock Text="{Binding SelectedTopicDetail.Topic.Name}" FontWeight="Bold" />
                    <ItemsControl Items="{Binding SelectedTopicDetail.Partitions}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="Auto,*,*" Margin="0,2">
                                    <TextBlock Grid.Column="0" Text="{Binding PartitionId}" Width="30" />
                                    <TextBlock Grid.Column="1" Text="{Binding EarliestOffset}" />
                                    <TextBlock Grid.Column="2" Text="{Binding LatestOffset}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>
        </Grid>
    </DockPanel>
</UserControl>
```

`src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml.cs`:
```csharp
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Topics.ViewModels;

namespace Skat.KawkaProject.Features.Topics.Views;

public partial class TopicsView : ReactiveUserControl<TopicsViewModel>
{
    public TopicsView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 6: Wire NavigateToTopicsCommand in ConnectionNodeViewModel**

In `ConnectionNodeViewModel.cs`, replace the `NavigateToTopicsCommand` lambda:
```csharp
NavigateToTopicsCommand = ReactiveCommand.Create(() =>
{
    if (_session == null) return;
    shell.Router.Navigate.Execute(
        new Skat.KawkaProject.Features.Topics.ViewModels.TopicsViewModel(shell, _session, topicService));
});
```

Add the parameter to the constructor signature (already present — just fill in the body).

- [ ] **Step 7: Run tests**

```bash
dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "TopicsViewModel"
```
Expected: 2 tests pass.

- [ ] **Step 8: Build full solution**

```bash
dotnet build src/Skat.KawkaProject.sln
```
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics src/Skat.KawkaProject.Features.Tests src/Skat.KawkaProject.UI
git commit -m "feat: add Topics feature with list, detail panel, create and delete"
```

---

## Task 9: Features.Messages

**Files:**
- Create: `src/Skat.KawkaProject.Features.Messages/` (project, VM, View)
- Modify: `src/Skat.KawkaProject.Features.Tests/MessagesViewModelTests.cs`
- Modify: `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`
- Modify: `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

**Interfaces:**
- Consumes: `IMessageService`, `IKafkaSession`, `KafkaMessage` from Core
- Produces: `MessagesViewModel` navigable via router

- [ ] **Step 1: Create project**

```bash
cd src
dotnet new classlib -n Skat.KawkaProject.Features.Messages -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Features.Messages/Skat.KawkaProject.Features.Messages.csproj
rm Skat.KawkaProject.Features.Messages/Class1.cs
dotnet add Skat.KawkaProject.Features.Messages/Skat.KawkaProject.Features.Messages.csproj reference Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj
dotnet add Skat.KawkaProject.Features.Messages/Skat.KawkaProject.Features.Messages.csproj package Avalonia --version 0.10.3
dotnet add Skat.KawkaProject.Features.Messages/Skat.KawkaProject.Features.Messages.csproj package Avalonia.ReactiveUI --version 0.10.3
dotnet add Skat.KawkaProject.Features.Messages/Skat.KawkaProject.Features.Messages.csproj package System.Reactive --version 6.0.1
dotnet add Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj reference Skat.KawkaProject.Features.Messages/Skat.KawkaProject.Features.Messages.csproj
dotnet add Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj reference Skat.KawkaProject.Features.Messages/Skat.KawkaProject.Features.Messages.csproj
mkdir -p Skat.KawkaProject.Features.Messages/ViewModels
mkdir -p Skat.KawkaProject.Features.Messages/Views
```

- [ ] **Step 2: Write failing tests**

`src/Skat.KawkaProject.Features.Tests/MessagesViewModelTests.cs`:
```csharp
using System;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Messages.ViewModels;
using Xunit;

namespace Skat.KawkaProject.Features.Tests;

public class MessagesViewModelTests
{
    private static IScreen FakeScreen()
    {
        var mock = new Mock<IScreen>();
        mock.Setup(s => s.Router).Returns(new RoutingState());
        return mock.Object;
    }

    private static IKafkaSession FakeSession() => new Mock<IKafkaSession>().Object;

    private static KafkaMessage Msg(string value) =>
        new("topic", 0, 0, null, value, DateTime.UtcNow);

    [Fact]
    public async Task FetchMessages_populates_Messages_in_offset_mode()
    {
        var svc = new Mock<IMessageService>();
        svc.Setup(s => s.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "t", 0, 0, 50))
           .ReturnsAsync(new[] { Msg("hello"), Msg("world") });

        var vm = new MessagesViewModel(FakeScreen(), FakeSession(), svc.Object);
        vm.TopicName = "t";
        await vm.FetchMessagesAsync();

        Assert.Equal(2, vm.Messages.Count);
    }

    [Fact]
    public void StartTail_adds_live_messages_to_top_of_collection()
    {
        var subject = new Subject<KafkaMessage>();
        var svc = new Mock<IMessageService>();
        svc.Setup(s => s.Tail(It.IsAny<IKafkaSession>(), It.IsAny<string>()))
           .Returns(subject);

        var vm = new MessagesViewModel(FakeScreen(), FakeSession(), svc.Object);
        vm.TopicName = "live-topic";
        vm.StartTail();

        subject.OnNext(Msg("first"));
        subject.OnNext(Msg("second"));

        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal("second", vm.Messages[0].Value);
    }

    [Fact]
    public void PauseTail_stops_adding_messages_without_unsubscribing()
    {
        var subject = new Subject<KafkaMessage>();
        var svc = new Mock<IMessageService>();
        svc.Setup(s => s.Tail(It.IsAny<IKafkaSession>(), It.IsAny<string>()))
           .Returns(subject);

        var vm = new MessagesViewModel(FakeScreen(), FakeSession(), svc.Object);
        vm.TopicName = "paused-topic";
        vm.StartTail();
        vm.PauseTail();

        subject.OnNext(Msg("while-paused"));

        Assert.Empty(vm.Messages);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

```bash
dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "MessagesViewModel"
```
Expected: Build error.

- [ ] **Step 4: Implement MessagesViewModel**

`src/Skat.KawkaProject.Features.Messages/ViewModels/MessagesViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Messages.ViewModels;

public enum MessageMode { Offset, Tail }

public class MessagesViewModel : ReactiveObject, IRoutableViewModel
{
    private readonly IKafkaSession _session;
    private readonly IMessageService _messageService;
    private IDisposable? _tailSubscription;
    private bool _isPaused;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _selectedMessageValue;
    private string _topicName = "";
    private int _partition;
    private long _startOffset;
    private int _fetchCount = 50;
    private MessageMode _mode = MessageMode.Offset;
    private string _clientFilter = "";

    public IScreen HostScreen { get; }
    public string UrlPathSegment => "messages";

    public ObservableCollection<KafkaMessage> Messages { get; } = new();
    public ICommand FetchCommand { get; }
    public ICommand StartTailCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopTailCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public IEnumerable<MessageMode> Modes => Enum.GetValues<MessageMode>();

    public string TopicName { get => _topicName; set => this.RaiseAndSetIfChanged(ref _topicName, value); }
    public int Partition { get => _partition; set => this.RaiseAndSetIfChanged(ref _partition, value); }
    public long StartOffset { get => _startOffset; set => this.RaiseAndSetIfChanged(ref _startOffset, value); }
    public int FetchCount { get => _fetchCount; set => this.RaiseAndSetIfChanged(ref _fetchCount, value); }
    public MessageMode Mode { get => _mode; set => this.RaiseAndSetIfChanged(ref _mode, value); }
    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public bool IsPaused { get => _isPaused; private set => this.RaiseAndSetIfChanged(ref _isPaused, value); }
    public string? ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }
    private KafkaMessage? _selectedMessage;
    public KafkaMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMessage, value);
            SelectedMessageValue = FormatValue(value?.Value);
        }
    }

    public string? SelectedMessageValue { get => _selectedMessageValue; private set => this.RaiseAndSetIfChanged(ref _selectedMessageValue, value); }

    private static string? FormatValue(string? raw)
    {
        if (raw == null) return null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(doc,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { return raw; }
    }

    public string ClientFilter
    {
        get => _clientFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _clientFilter, value);
            this.RaisePropertyChanged(nameof(FilteredMessages));
        }
    }

    public IEnumerable<KafkaMessage> FilteredMessages =>
        string.IsNullOrWhiteSpace(_clientFilter)
            ? Messages
            : Messages.Where(m =>
                (m.Value?.Contains(_clientFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.Key?.Contains(_clientFilter, StringComparison.OrdinalIgnoreCase) ?? false));

    public MessagesViewModel(IScreen hostScreen, IKafkaSession session, IMessageService messageService)
    {
        HostScreen = hostScreen;
        _session = session;
        _messageService = messageService;

        FetchCommand = ReactiveCommand.CreateFromTask(FetchMessagesAsync);
        StartTailCommand = ReactiveCommand.Create(StartTail);
        PauseCommand = ReactiveCommand.Create(PauseTail);
        StopTailCommand = ReactiveCommand.Create(StopTail);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);
    }

    public async Task FetchMessagesAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var results = await _messageService.FetchMessagesAsync(
                _session, _topicName, _partition, _startOffset, _fetchCount);
            Messages.Clear();
            foreach (var m in results) Messages.Add(m);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public void StartTail()
    {
        StopTail();
        IsPaused = false;
        ErrorMessage = null;
        _tailSubscription = _messageService
            .Tail(_session, _topicName)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                m => { if (!_isPaused) { Messages.Insert(0, m); this.RaisePropertyChanged(nameof(FilteredMessages)); } },
                ex => ErrorMessage = ex.Message);
    }

    public void PauseTail() => IsPaused = true;

    public void StopTail()
    {
        _tailSubscription?.Dispose();
        _tailSubscription = null;
        IsPaused = false;
    }
}
```

- [ ] **Step 5: Create MessagesView.axaml**

`src/Skat.KawkaProject.Features.Messages/Views/MessagesView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Skat.KawkaProject.Features.Messages.ViewModels"
             x:Class="Skat.KawkaProject.Features.Messages.Views.MessagesView">
    <DockPanel>
        <!-- Toolbar -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="8">
            <TextBox Text="{Binding TopicName}" Watermark="Topic name" Width="180" />
            <ComboBox Items="{Binding Modes}" SelectedItem="{Binding Mode}" />
            <!-- Offset mode controls -->
            <StackPanel Orientation="Horizontal" Spacing="4"
                        IsVisible="{Binding Mode, Converter={x:Static ObjectConverters.IsEqual}, ConverterParameter={x:Static vm:MessageMode.Offset}}">
                <TextBlock VerticalAlignment="Center">Partition:</TextBlock>
                <NumericUpDown Value="{Binding Partition}" Minimum="0" Width="70" />
                <TextBlock VerticalAlignment="Center">From offset:</TextBlock>
                <NumericUpDown Value="{Binding StartOffset}" Minimum="0" Width="90" />
                <TextBlock VerticalAlignment="Center">Count:</TextBlock>
                <NumericUpDown Value="{Binding FetchCount}" Minimum="1" Maximum="1000" Width="70" />
                <Button Command="{Binding FetchCommand}">Fetch</Button>
            </StackPanel>
            <!-- Tail mode controls -->
            <StackPanel Orientation="Horizontal" Spacing="4"
                        IsVisible="{Binding Mode, Converter={x:Static ObjectConverters.IsEqual}, ConverterParameter={x:Static vm:MessageMode.Tail}}">
                <Button Command="{Binding StartTailCommand}">Tail</Button>
                <Button Command="{Binding PauseCommand}">Pause</Button>
                <Button Command="{Binding StopTailCommand}">Stop</Button>
            </StackPanel>
            <TextBox Text="{Binding ClientFilter}" Watermark="Filter loaded..." Width="160" />
        </StackPanel>

        <!-- Error banner (dismissible) -->
        <Border DockPanel.Dock="Top" Background="#FFDDDD" Padding="8"
                IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Text="{Binding ErrorMessage}" Foreground="DarkRed" TextWrapping="Wrap" />
                <Button Grid.Column="1" Command="{Binding DismissErrorCommand}" Padding="4,0">✕</Button>
            </Grid>
        </Border>
        <ProgressBar DockPanel.Dock="Top" IsIndeterminate="True" IsVisible="{Binding IsBusy}" Height="4" />

        <!-- Message list + detail -->
        <Grid RowDefinitions="*,200">
            <ListBox Grid.Row="0" Items="{Binding FilteredMessages}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="60,60,140,*">
                            <TextBlock Grid.Column="0" Text="{Binding Partition}" />
                            <TextBlock Grid.Column="1" Text="{Binding Offset}" />
                            <TextBlock Grid.Column="2" Text="{Binding Timestamp, StringFormat='{}{0:HH:mm:ss.fff}'}" />
                            <TextBlock Grid.Column="3" Text="{Binding Value}" TextTrimming="CharacterEllipsis" />
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
            <Border Grid.Row="1" Padding="8" BorderBrush="#CCCCCC" BorderThickness="0,1,0,0">
                <ScrollViewer>
                    <TextBlock Text="{Binding SelectedMessageValue}" TextWrapping="Wrap" FontFamily="Monospace" />
                </ScrollViewer>
            </Border>
        </Grid>
    </DockPanel>
</UserControl>
```

`src/Skat.KawkaProject.Features.Messages/Views/MessagesView.axaml.cs`:
```csharp
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Messages.ViewModels;

namespace Skat.KawkaProject.Features.Messages.Views;

public partial class MessagesView : ReactiveUserControl<MessagesViewModel>
{
    public MessagesView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 6: Wire NavigateToMessagesCommand in ConnectionNodeViewModel**

```csharp
NavigateToMessagesCommand = ReactiveCommand.Create(() =>
{
    if (_session == null) return;
    shell.Router.Navigate.Execute(
        new Skat.KawkaProject.Features.Messages.ViewModels.MessagesViewModel(shell, _session, messageService));
});
```

- [ ] **Step 7: Run tests**

```bash
dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "MessagesViewModel"
```
Expected: 3 tests pass.

- [ ] **Step 8: Build solution**

```bash
dotnet build src/Skat.KawkaProject.sln
```
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add src/Skat.KawkaProject.Features.Messages src/Skat.KawkaProject.Features.Tests src/Skat.KawkaProject.UI
git commit -m "feat: add Messages feature with offset fetch and live tail modes"
```

---

## Task 10: Features.Cluster

**Files:**
- Create: `src/Skat.KawkaProject.Features.Cluster/` (project, VM, View)
- Modify: `src/Skat.KawkaProject.Features.Tests/ClusterViewModelTests.cs`
- Modify: `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`
- Modify: `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

**Interfaces:**
- Consumes: `IClusterService`, `IKafkaSession`, `BrokerInfo`, `ConsumerGroupInfo`, `PartitionLag` from Core
- Produces: `ClusterViewModel` navigable via router

- [ ] **Step 1: Create project**

```bash
cd src
dotnet new classlib -n Skat.KawkaProject.Features.Cluster -f net6.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Features.Cluster/Skat.KawkaProject.Features.Cluster.csproj
rm Skat.KawkaProject.Features.Cluster/Class1.cs
dotnet add Skat.KawkaProject.Features.Cluster/Skat.KawkaProject.Features.Cluster.csproj reference Skat.KawkaProject.Core/Skat.KawkaProject.Core.csproj
dotnet add Skat.KawkaProject.Features.Cluster/Skat.KawkaProject.Features.Cluster.csproj package Avalonia --version 0.10.3
dotnet add Skat.KawkaProject.Features.Cluster/Skat.KawkaProject.Features.Cluster.csproj package Avalonia.ReactiveUI --version 0.10.3
dotnet add Skat.KawkaProject.Features.Cluster/Skat.KawkaProject.Features.Cluster.csproj package System.Reactive --version 6.0.1
dotnet add Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj reference Skat.KawkaProject.Features.Cluster/Skat.KawkaProject.Features.Cluster.csproj
dotnet add Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj reference Skat.KawkaProject.Features.Cluster/Skat.KawkaProject.Features.Cluster.csproj
mkdir -p Skat.KawkaProject.Features.Cluster/ViewModels
mkdir -p Skat.KawkaProject.Features.Cluster/Views
```

- [ ] **Step 2: Write failing tests**

`src/Skat.KawkaProject.Features.Tests/ClusterViewModelTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Cluster.ViewModels;
using Xunit;

namespace Skat.KawkaProject.Features.Tests;

public class ClusterViewModelTests
{
    private static IScreen FakeScreen()
    {
        var mock = new Mock<IScreen>();
        mock.Setup(s => s.Router).Returns(new RoutingState());
        return mock.Object;
    }

    private static IKafkaSession FakeSession() => new Mock<IKafkaSession>().Object;

    [Fact]
    public async Task LoadAsync_populates_Brokers()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListBrokersAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new BrokerInfo(1, "localhost", 9092, true) });
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(Array.Empty<ConsumerGroupInfo>());

        var vm = new ClusterViewModel(FakeScreen(), FakeSession(), svc.Object);
        await vm.LoadAsync();

        Assert.Single(vm.Brokers);
        Assert.Equal("localhost", vm.Brokers[0].Host);
    }

    [Fact]
    public async Task LoadLagAsync_populates_Lag_for_selected_group()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListBrokersAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(Array.Empty<BrokerInfo>());
        svc.Setup(s => s.ListConsumerGroupsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new ConsumerGroupInfo("my-group", "Stable", 2) });
        svc.Setup(s => s.GetGroupLagAsync(It.IsAny<IKafkaSession>(), "my-group"))
           .ReturnsAsync(new[] { new PartitionLag("orders", 0, 5, 10, 5) });

        var vm = new ClusterViewModel(FakeScreen(), FakeSession(), svc.Object);
        await vm.LoadAsync();
        vm.SelectedGroup = vm.ConsumerGroups[0];
        await vm.LoadLagAsync();

        Assert.Single(vm.Lag);
        Assert.Equal(5, vm.Lag[0].Lag);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

```bash
dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "ClusterViewModel"
```
Expected: Build error.

- [ ] **Step 4: Implement ClusterViewModel**

`src/Skat.KawkaProject.Features.Cluster/ViewModels/ClusterViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Cluster.ViewModels;

public class ClusterViewModel : ReactiveObject, IRoutableViewModel
{
    private readonly IKafkaSession _session;
    private readonly IClusterService _clusterService;
    private bool _isBusy;
    private string? _errorMessage;
    private ConsumerGroupInfo? _selectedGroup;
    private IDisposable? _autoRefresh;

    public IScreen HostScreen { get; }
    public string UrlPathSegment => "cluster";

    public ObservableCollection<BrokerInfo> Brokers { get; } = new();
    public ObservableCollection<ConsumerGroupInfo> ConsumerGroups { get; } = new();
    public ObservableCollection<PartitionLag> Lag { get; } = new();

    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public string? ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }

    public ConsumerGroupInfo? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGroup, value);
            Lag.Clear();
        }
    }

    public ICommand LoadCommand { get; }
    public ICommand LoadLagCommand { get; }
    public ICommand DismissErrorCommand { get; }

    public ClusterViewModel(IScreen hostScreen, IKafkaSession session, IClusterService clusterService)
    {
        HostScreen = hostScreen;
        _session = session;
        _clusterService = clusterService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        LoadLagCommand = ReactiveCommand.CreateFromTask(LoadLagAsync);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);

        // Auto-refresh lag every 10 seconds
        _autoRefresh = Observable.Interval(TimeSpan.FromSeconds(10))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async _ =>
            {
                if (_selectedGroup != null) await LoadLagAsync();
            });

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var brokers = await _clusterService.ListBrokersAsync(_session);
            var groups = await _clusterService.ListConsumerGroupsAsync(_session);
            Brokers.Clear();
            foreach (var b in brokers) Brokers.Add(b);
            ConsumerGroups.Clear();
            foreach (var g in groups) ConsumerGroups.Add(g);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task LoadLagAsync()
    {
        if (_selectedGroup == null) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var lag = await _clusterService.GetGroupLagAsync(_session, _selectedGroup.GroupId);
            Lag.Clear();
            foreach (var l in lag) Lag.Add(l);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
```

- [ ] **Step 5: Create ClusterView.axaml**

`src/Skat.KawkaProject.Features.Cluster/Views/ClusterView.axaml`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Skat.KawkaProject.Features.Cluster.Views.ClusterView">
    <DockPanel>
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="8">
            <Button Command="{Binding LoadCommand}">Refresh</Button>
        </StackPanel>
        <Border DockPanel.Dock="Top" Background="#FFDDDD" Padding="8"
                IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <TextBlock Text="{Binding ErrorMessage}" Foreground="DarkRed" />
        </Border>
        <ProgressBar DockPanel.Dock="Top" IsIndeterminate="True" IsVisible="{Binding IsBusy}" Height="4" />

        <TabControl>
            <!-- Brokers tab -->
            <TabItem Header="Brokers">
                <ListBox Items="{Binding Brokers}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid ColumnDefinitions="60,*,80,80">
                                <TextBlock Grid.Column="0" Text="{Binding BrokerId}" />
                                <TextBlock Grid.Column="1" Text="{Binding Host}" />
                                <TextBlock Grid.Column="2" Text="{Binding Port}" />
                                <TextBlock Grid.Column="3" Text="{Binding IsController}" />
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </TabItem>

            <!-- Consumer Groups tab -->
            <TabItem Header="Consumer Groups">
                <ListBox Items="{Binding ConsumerGroups}" SelectedItem="{Binding SelectedGroup}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid ColumnDefinitions="*,100,60">
                                <TextBlock Grid.Column="0" Text="{Binding GroupId}" />
                                <TextBlock Grid.Column="1" Text="{Binding State}" />
                                <TextBlock Grid.Column="2" Text="{Binding MemberCount}" />
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </TabItem>

            <!-- Lag tab -->
            <TabItem Header="Lag">
                <DockPanel>
                    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="8">
                        <ComboBox Items="{Binding ConsumerGroups}" SelectedItem="{Binding SelectedGroup}"
                                  DisplayMemberBinding="{Binding GroupId}" Width="200" />
                        <Button Command="{Binding LoadLagCommand}">Load Lag</Button>
                        <TextBlock VerticalAlignment="Center" Foreground="Gray">Auto-refreshes every 10s</TextBlock>
                    </StackPanel>
                    <ListBox Items="{Binding Lag}">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,60,100,100,80">
                                    <TextBlock Grid.Column="0" Text="{Binding Topic}" />
                                    <TextBlock Grid.Column="1" Text="{Binding Partition}" />
                                    <TextBlock Grid.Column="2" Text="{Binding CurrentOffset}" />
                                    <TextBlock Grid.Column="3" Text="{Binding EndOffset}" />
                                    <TextBlock Grid.Column="4" Text="{Binding Lag}" FontWeight="Bold" />
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
            </TabItem>
        </TabControl>
    </DockPanel>
</UserControl>
```

`src/Skat.KawkaProject.Features.Cluster/Views/ClusterView.axaml.cs`:
```csharp
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Cluster.ViewModels;

namespace Skat.KawkaProject.Features.Cluster.Views;

public partial class ClusterView : ReactiveUserControl<ClusterViewModel>
{
    public ClusterView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 6: Wire NavigateToClusterCommand in ConnectionNodeViewModel**

```csharp
NavigateToClusterCommand = ReactiveCommand.Create(() =>
{
    if (_session == null) return;
    shell.Router.Navigate.Execute(
        new Skat.KawkaProject.Features.Cluster.ViewModels.ClusterViewModel(shell, _session, clusterService));
});
```

- [ ] **Step 7: Run all tests**

```bash
dotnet test src/Skat.KawkaProject.sln
```
Expected: All tests pass (unit + integration).

- [ ] **Step 8: Build full solution**

```bash
dotnet build src/Skat.KawkaProject.sln
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/Skat.KawkaProject.Features.Cluster src/Skat.KawkaProject.Features.Tests src/Skat.KawkaProject.UI
git commit -m "feat: add Cluster feature with brokers, consumer groups, and lag monitoring"
```
