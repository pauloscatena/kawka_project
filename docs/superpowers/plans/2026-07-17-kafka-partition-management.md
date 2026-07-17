# Kafka Partition Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users increase a topic's partition count, reduce it via a guarded delete+recreate (preserving topic config), and jump from a partition row straight to that partition's messages.

**Architecture:** Two new `ITopicService`/`TopicService` methods (`GetTopicConfigAsync`, `RecreateTopicWithFewerPartitionsAsync`) built on the existing Confluent.Kafka `AdminClient`. `TopicsViewModel` gains state/commands for both partition operations plus a `ViewPartitionMessagesCommand` that calls an injected navigation callback (kept as a callback, not a direct `MessagesViewModel` reference, because `Skat.KawkaProject.Features.Topics` has no project reference to `Skat.KawkaProject.Features.Messages` — cross-feature composition happens only in the UI layer's `ConnectionNodeViewModel`, matching the existing pattern). `TopicsView.axaml` gets two new inline forms (mirroring the existing "New Topic" form) and a per-partition-row button.

**Tech Stack:** .NET 10, Avalonia 11.3.9 + ReactiveUI, Confluent.Kafka 2.3.0, xUnit + Moq (unit tests), Testcontainers.Kafka (integration tests).

## Global Constraints

- Target framework: `net10.0` for all projects (already set — do not change).
- `Skat.KawkaProject.Features.Topics` must NOT gain a project reference to `Skat.KawkaProject.Features.Messages`; navigation crosses that boundary via an injected `Action<string, int>` callback, composed in `Skat.KawkaProject.UI`.
- Follow the existing ReactiveUI patterns already in `TopicsViewModel`/`MessagesViewModel`: `ReactiveCommand.Create`/`CreateFromTask`, `IsBusy`/`ErrorMessage` handling identical in shape to `CreateTopicAsync`/`DeleteTopicAsync`.
- Follow the existing AXAML visual language in `TopicsView.axaml`/`MessagesView.axaml`: `DynamicResource` brushes (`SurfaceBrush`, `AccentBrush`, `AccentSubtleBrush`, `DestructiveBrush`, `StatusErrorBrush`, `DestructiveTextBrush`, `TextMutedBrush`, `TextPrimaryBrush`, `BorderBrush`), FontSize 11 for body text / 10 for labels, `Padding="8,4"` for buttons.
- Integration tests requiring Testcontainers need Docker running locally; they are slower and are the existing pattern in `Skat.KawkaProject.Kafka.Tests`.

---

### Task 1: TopicService — get topic config & recreate with fewer partitions

**Files:**
- Modify: `src/Skat.KawkaProject.Core/Interfaces/ITopicService.cs`
- Modify: `src/Skat.KawkaProject.Kafka/TopicService.cs`
- Modify: `src/Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`

**Interfaces:**
- Consumes: `AdminClientBuilder`, `AdminClient.DescribeConfigsAsync(IEnumerable<ConfigResource>, DescribeConfigsOptions?)`, `AdminClient.DeleteTopicsAsync(IEnumerable<string>, DeleteTopicsOptions?)`, `AdminClient.CreateTopicsAsync(IEnumerable<TopicSpecification>, CreateTopicsOptions?)`, `AdminClient.GetMetadata(TimeSpan)` (all already used elsewhere in `TopicService.cs`); `Confluent.Kafka.Admin.ConfigResource { ResourceType Type; string Name; }`, `Confluent.Kafka.Admin.ResourceType.Topic`, `Confluent.Kafka.Admin.ConfigEntryResult { string Name; string Value; bool IsDefault; }` (verified via reflection against the installed `Confluent.Kafka 2.3.0` package — these are the real property/field names).
- Produces: `Task<IReadOnlyDictionary<string, string>> ITopicService.GetTopicConfigAsync(IKafkaSession session, string topicName)` and `Task ITopicService.RecreateTopicWithFewerPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor)` — used by Task 4.

- [ ] **Step 1: Write the failing integration tests**

Add to `src/Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`. First replace the `using` block at the very top of the file:

```csharp
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;
```

with:

```csharp
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Kafka;
using Testcontainers.Kafka;
```

Then add these two test methods inside the `TopicServiceIntegrationTests` class, after `GetTopicDetailAsync_returns_partition_offsets`:

```csharp
    [Fact]
    public async Task GetTopicConfigAsync_returns_overridden_config_values()
    {
        using var session = Session();
        var adminCfg = new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using (var admin = new AdminClientBuilder(adminCfg).Build())
        {
            await admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = "config-topic",
                    NumPartitions = 1,
                    ReplicationFactor = 1,
                    Configs = new Dictionary<string, string> { ["retention.ms"] = "3600000" }
                }
            });
        }

        var svc = new TopicService();
        var config = await svc.GetTopicConfigAsync(session, "config-topic");

        Assert.Equal("3600000", config["retention.ms"]);
    }

    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_reduces_partitions_and_preserves_config()
    {
        using var session = Session();
        var adminCfg = new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using (var admin = new AdminClientBuilder(adminCfg).Build())
        {
            await admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = "shrink-topic",
                    NumPartitions = 4,
                    ReplicationFactor = 1,
                    Configs = new Dictionary<string, string> { ["retention.ms"] = "7200000" }
                }
            });
        }

        var svc = new TopicService();
        await svc.RecreateTopicWithFewerPartitionsAsync(session, "shrink-topic", 2, 1);

        var detail = await svc.GetTopicDetailAsync(session, "shrink-topic");
        Assert.Equal(2, detail.Partitions.Count);

        var config = await svc.GetTopicConfigAsync(session, "shrink-topic");
        Assert.Equal("7200000", config["retention.ms"]);
    }
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj --filter "FullyQualifiedName~GetTopicConfigAsync_returns_overridden_config_values|FullyQualifiedName~RecreateTopicWithFewerPartitionsAsync_reduces_partitions_and_preserves_config"`

Expected: build error — `ITopicService` / `TopicService` do not contain definitions for `GetTopicConfigAsync` or `RecreateTopicWithFewerPartitionsAsync`.

- [ ] **Step 3: Add the two methods to `ITopicService`**

In `src/Skat.KawkaProject.Core/Interfaces/ITopicService.cs`, replace:

```csharp
public interface ITopicService
{
    Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session);
    Task<TopicDetail> GetTopicDetailAsync(IKafkaSession session, string topicName);
    Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor);
    Task DeleteTopicAsync(IKafkaSession session, string topicName);
    Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount);
}
```

with:

```csharp
public interface ITopicService
{
    Task<IEnumerable<TopicInfo>> ListTopicsAsync(IKafkaSession session);
    Task<TopicDetail> GetTopicDetailAsync(IKafkaSession session, string topicName);
    Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor);
    Task DeleteTopicAsync(IKafkaSession session, string topicName);
    Task ExpandPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount);
    Task<IReadOnlyDictionary<string, string>> GetTopicConfigAsync(IKafkaSession session, string topicName);
    Task RecreateTopicWithFewerPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor);
}
```

- [ ] **Step 4: Implement the methods in `TopicService`**

In `src/Skat.KawkaProject.Kafka/TopicService.cs`, add these methods at the end of the class, just before the closing `}`:

```csharp
    public async Task<IReadOnlyDictionary<string, string>> GetTopicConfigAsync(IKafkaSession session, string topicName)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var results = await admin.DescribeConfigsAsync(new[]
        {
            new ConfigResource { Type = ResourceType.Topic, Name = topicName }
        });
        return results[0].Entries.Values
            .Where(e => !e.IsDefault)
            .ToDictionary(e => e.Name, e => e.Value);
    }

    public async Task RecreateTopicWithFewerPartitionsAsync(
        IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor)
    {
        var config = await GetTopicConfigAsync(session, topicName);

        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.DeleteTopicsAsync(new[] { topicName });
        await WaitForTopicDeletionAsync(admin, topicName);

        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = topicName,
                NumPartitions = newPartitionCount,
                ReplicationFactor = replicationFactor,
                Configs = new Dictionary<string, string>(config)
            }
        });
    }

    private static async Task WaitForTopicDeletionAsync(IAdminClient admin, string topicName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var meta = await Task.Run(() => admin.GetMetadata(TimeSpan.FromSeconds(10)));
            if (!meta.Topics.Any(t => t.Topic == topicName)) return;
            await Task.Delay(300);
        }
        throw new TimeoutException($"Timed out waiting for topic '{topicName}' deletion before recreate.");
    }
```

This uses `System.Linq` (`.Where`, `.ToDictionary`, `.Any`) — confirm the top of `TopicService.cs` still only needs `Confluent.Kafka`, `Confluent.Kafka.Admin`, `Skat.KawkaProject.Core.Interfaces`, `Skat.KawkaProject.Core.Models` since `ImplicitUsings` is enabled for the project (implicit `System.Linq` is included by the SDK default global usings).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj --filter "FullyQualifiedName~GetTopicConfigAsync_returns_overridden_config_values|FullyQualifiedName~RecreateTopicWithFewerPartitionsAsync_reduces_partitions_and_preserves_config"`

Expected: PASS (requires Docker running for Testcontainers; both tests spin up a real Kafka broker).

- [ ] **Step 6: Run the full Kafka.Tests suite to check for regressions**

Run: `dotnet test src/Skat.KawkaProject.Kafka.Tests/Skat.KawkaProject.Kafka.Tests.csproj`

Expected: PASS (all tests, including the 3 pre-existing ones).

- [ ] **Step 7: Commit**

```bash
git add src/Skat.KawkaProject.Core/Interfaces/ITopicService.cs src/Skat.KawkaProject.Kafka/TopicService.cs src/Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs
git commit -m "feat(kafka): add GetTopicConfigAsync and RecreateTopicWithFewerPartitionsAsync"
```

---

### Task 2: TopicsViewModel — navigation callback + ViewPartitionMessagesCommand

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Modify: `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`
- Modify: `src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: existing `TopicsViewModel` constructor `(IScreen hostScreen, IKafkaSession session, ITopicService topicService)`; existing `MessagesViewModel(IScreen hostScreen, IKafkaSession session, IMessageService messageService, ITopicService topicService)` and its settable `TopicName`, `Partition`, `Mode` properties and public `FetchMessagesAsync()` method (all pre-existing, from `src/Skat.KawkaProject.Features.Messages/ViewModels/MessagesViewModel.cs`).
- Produces: new `TopicsViewModel` constructor `(IScreen hostScreen, IKafkaSession session, ITopicService topicService, Action<string, int> onViewPartitionMessages)` and `public void ViewPartitionMessages(int partition)` / `ICommand ViewPartitionMessagesCommand` — this constructor shape is what Tasks 3 and 4 build on, and what Task 7's AXAML binds `ViewPartitionMessagesCommand` to.

This task changes the `TopicsViewModel` constructor signature, so it must land before Tasks 3 and 4 (which add more commands to the same constructor body) and before Task 7 (which binds `ViewPartitionMessagesCommand` in the view).

- [ ] **Step 1: Write the failing unit test**

In `src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`, first update the two existing tests' constructor calls (they currently pass 3 args) and add a shared no-op callback field. Replace the whole file content with:

```csharp
using Moq;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Features.Topics.ViewModels;

namespace Skat.KawkaProject.Features.Tests;

public class TopicsViewModelTests
{
    private static readonly Action<string, int> NoOpNavigate = (_, _) => { };

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

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
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

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        await vm.DeleteTopicAsync("to-delete");

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "to-delete"), Times.Once);
    }

    [Fact]
    public async Task ViewPartitionMessages_invokes_callback_with_selected_topic_and_partition()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(
               new TopicInfo("orders", 2, 1),
               new List<PartitionInfo> { new(0, 1, 0, 10), new(1, 1, 0, 5) }));

        string? capturedTopic = null;
        int? capturedPartition = null;
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object,
            (topic, partition) => { capturedTopic = topic; capturedPartition = partition; });

        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ViewPartitionMessages(1);

        Assert.Equal("orders", capturedTopic);
        Assert.Equal(1, capturedPartition);
    }

    [Fact]
    public void ViewPartitionMessages_does_nothing_when_no_topic_selected()
    {
        var svc = new Mock<ITopicService>();
        var called = false;
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object,
            (_, _) => called = true);

        vm.ViewPartitionMessages(0);

        Assert.False(called);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "FullyQualifiedName~TopicsViewModelTests"`

Expected: build error — no constructor for `TopicsViewModel` takes 4 arguments; `ViewPartitionMessages` does not exist.

- [ ] **Step 3: Update `TopicsViewModel` constructor and add `ViewPartitionMessages`**

In `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, replace the field declarations and constructor:

```csharp
    private readonly IKafkaSession _session;
    private readonly ITopicService _topicService;
    private bool _isBusy;
```

with:

```csharp
    private readonly IKafkaSession _session;
    private readonly ITopicService _topicService;
    private readonly Action<string, int> _onViewPartitionMessages;
    private bool _isBusy;
```

Then replace:

```csharp
    public ICommand LoadCommand { get; }
    public ICommand ShowCreateFormCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand CreateTopicCommand { get; }
    public ICommand DeleteTopicCommand { get; }
    public ICommand DismissErrorCommand { get; }
```

with:

```csharp
    public ICommand LoadCommand { get; }
    public ICommand ShowCreateFormCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand CreateTopicCommand { get; }
    public ICommand DeleteTopicCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public ICommand ViewPartitionMessagesCommand { get; }
```

Then replace the constructor:

```csharp
    public TopicsViewModel(IScreen hostScreen, IKafkaSession session, ITopicService topicService)
    {
        HostScreen = hostScreen;
        _session = session;
        _topicService = topicService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadTopicsAsync);
        DeleteTopicCommand = ReactiveCommand.CreateFromTask<string>(DeleteTopicAsync);
        ShowCreateFormCommand = ReactiveCommand.Create(() => { IsCreatingTopic = true; NewTopicName = ""; NewTopicPartitions = 1; NewTopicReplicationFactor = 1; });
        CancelCreateCommand = ReactiveCommand.Create(() => IsCreatingTopic = false);
        CreateTopicCommand = ReactiveCommand.CreateFromTask(CreateTopicAsync);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);

        _ = LoadTopicsAsync();
    }
```

with:

```csharp
    public TopicsViewModel(IScreen hostScreen, IKafkaSession session, ITopicService topicService, Action<string, int> onViewPartitionMessages)
    {
        HostScreen = hostScreen;
        _session = session;
        _topicService = topicService;
        _onViewPartitionMessages = onViewPartitionMessages;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadTopicsAsync);
        DeleteTopicCommand = ReactiveCommand.CreateFromTask<string>(DeleteTopicAsync);
        ShowCreateFormCommand = ReactiveCommand.Create(() => { IsCreatingTopic = true; NewTopicName = ""; NewTopicPartitions = 1; NewTopicReplicationFactor = 1; });
        CancelCreateCommand = ReactiveCommand.Create(() => IsCreatingTopic = false);
        CreateTopicCommand = ReactiveCommand.CreateFromTask(CreateTopicAsync);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);
        ViewPartitionMessagesCommand = ReactiveCommand.Create<int>(ViewPartitionMessages);

        _ = LoadTopicsAsync();
    }

    public void ViewPartitionMessages(int partition)
    {
        if (SelectedTopicDetail == null) return;
        _onViewPartitionMessages(SelectedTopicDetail.Topic.Name, partition);
    }
```

- [ ] **Step 4: Update the only other construction site**

In `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`, replace:

```csharp
        NavigateToTopicsCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Topics.ViewModels.TopicsViewModel(shell, _session, topicService));
        });
```

with:

```csharp
        NavigateToTopicsCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Topics.ViewModels.TopicsViewModel(
                    shell, _session, topicService,
                    (topicName, partition) =>
                    {
                        if (_session == null) return;
                        var messagesVm = new Skat.KawkaProject.Features.Messages.ViewModels.MessagesViewModel(
                            shell, _session, messageService, topicService)
                        {
                            TopicName = topicName,
                            Partition = partition,
                            Mode = Skat.KawkaProject.Features.Messages.ViewModels.MessageMode.Offset,
                        };
                        shell.Router.Navigate.Execute(messagesVm);
                        _ = messagesVm.FetchMessagesAsync();
                    }));
        });
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "FullyQualifiedName~TopicsViewModelTests"`

Expected: PASS (4 tests).

- [ ] **Step 6: Build the whole solution to catch any other broken call sites**

Run: `dotnet build src/Skat.KawkaProject.sln`

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "feat(topics): add ViewPartitionMessages navigation callback"
```

---

### Task 3: TopicsViewModel — increase partitions

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Modify: `src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: `ITopicService.ExpandPartitionsAsync(IKafkaSession, string, int)` (pre-existing), `TopicsViewModel` constructor from Task 2, `_topicService`, `_session`, `SelectedTopicDetail`, `LoadTopicsAsync()`, `LoadDetailAsync(string)` (all pre-existing private/internal members of the class).
- Produces: `bool IsExpandingPartitions`, `bool IsNotExpandingPartitions`, `int NewPartitionCount`, `ICommand ShowExpandFormCommand`, `ICommand CancelExpandCommand`, `ICommand ExpandPartitionsCommand`, `public Task ExpandPartitionsAsync()` — consumed by Task 5's AXAML bindings.

- [ ] **Step 1: Write the failing unit tests**

Add to `src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`, inside the `TopicsViewModelTests` class (after `ViewPartitionMessages_does_nothing_when_no_topic_selected`):

```csharp
    [Fact]
    public async Task ExpandPartitionsAsync_calls_service_and_reloads_detail()
    {
        var svc = new Mock<ITopicService>();
        var detailBefore = new TopicDetail(new TopicInfo("orders", 2, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0) });
        var detailAfter = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });

        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.SetupSequence(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(detailBefore)
           .ReturnsAsync(detailAfter);
        svc.Setup(s => s.ExpandPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 4))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.NewPartitionCount = 4;

        await vm.ExpandPartitionsAsync();

        svc.Verify(s => s.ExpandPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 4), Times.Once);
        Assert.Equal(4, vm.SelectedTopicDetail!.Topic.PartitionCount);
    }

    [Fact]
    public async Task ExpandPartitionsAsync_rejects_count_not_greater_than_current()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 2, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(detail);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.NewPartitionCount = 2;

        await vm.ExpandPartitionsAsync();

        svc.Verify(s => s.ExpandPartitionsAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.NotNull(vm.ErrorMessage);
    }
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "FullyQualifiedName~ExpandPartitions"`

Expected: build error — `TopicsViewModel` does not contain `NewPartitionCount` / `ExpandPartitionsAsync`.

- [ ] **Step 3: Add expand-partitions state and command**

In `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, replace:

```csharp
    private bool _isCreatingTopic;
    private string _newTopicName = "";
    private int _newTopicPartitions = 1;
    private int _newTopicReplicationFactor = 1;

    public bool IsCreatingTopic { get => _isCreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isCreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotCreatingTopic)); } }
    public bool IsNotCreatingTopic => !_isCreatingTopic;
    public string NewTopicName { get => _newTopicName; set => this.RaiseAndSetIfChanged(ref _newTopicName, value); }
    public int NewTopicPartitions { get => _newTopicPartitions; set => this.RaiseAndSetIfChanged(ref _newTopicPartitions, value); }
    public int NewTopicReplicationFactor { get => _newTopicReplicationFactor; set => this.RaiseAndSetIfChanged(ref _newTopicReplicationFactor, value); }
```

with:

```csharp
    private bool _isCreatingTopic;
    private string _newTopicName = "";
    private int _newTopicPartitions = 1;
    private int _newTopicReplicationFactor = 1;
    private bool _isExpandingPartitions;
    private int _newPartitionCount = 1;

    public bool IsCreatingTopic { get => _isCreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isCreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotCreatingTopic)); } }
    public bool IsNotCreatingTopic => !_isCreatingTopic;
    public string NewTopicName { get => _newTopicName; set => this.RaiseAndSetIfChanged(ref _newTopicName, value); }
    public int NewTopicPartitions { get => _newTopicPartitions; set => this.RaiseAndSetIfChanged(ref _newTopicPartitions, value); }
    public int NewTopicReplicationFactor { get => _newTopicReplicationFactor; set => this.RaiseAndSetIfChanged(ref _newTopicReplicationFactor, value); }
    public bool IsExpandingPartitions { get => _isExpandingPartitions; private set { this.RaiseAndSetIfChanged(ref _isExpandingPartitions, value); this.RaisePropertyChanged(nameof(IsNotExpandingPartitions)); } }
    public bool IsNotExpandingPartitions => !_isExpandingPartitions;
    public int NewPartitionCount { get => _newPartitionCount; set => this.RaiseAndSetIfChanged(ref _newPartitionCount, value); }
```

Replace:

```csharp
    public ICommand ViewPartitionMessagesCommand { get; }
```

with:

```csharp
    public ICommand ViewPartitionMessagesCommand { get; }
    public ICommand ShowExpandFormCommand { get; }
    public ICommand CancelExpandCommand { get; }
    public ICommand ExpandPartitionsCommand { get; }
```

In the constructor, replace:

```csharp
        ViewPartitionMessagesCommand = ReactiveCommand.Create<int>(ViewPartitionMessages);

        _ = LoadTopicsAsync();
    }
```

with:

```csharp
        ViewPartitionMessagesCommand = ReactiveCommand.Create<int>(ViewPartitionMessages);
        ShowExpandFormCommand = ReactiveCommand.Create(() =>
        {
            IsExpandingPartitions = true;
            NewPartitionCount = (SelectedTopicDetail?.Partitions.Count ?? 0) + 1;
        });
        CancelExpandCommand = ReactiveCommand.Create(() => IsExpandingPartitions = false);
        ExpandPartitionsCommand = ReactiveCommand.CreateFromTask(ExpandPartitionsAsync);

        _ = LoadTopicsAsync();
    }

    public async Task ExpandPartitionsAsync()
    {
        if (SelectedTopicDetail == null) return;
        var currentCount = SelectedTopicDetail.Partitions.Count;
        if (_newPartitionCount <= currentCount)
        {
            ErrorMessage = $"New partition count must be greater than the current count ({currentCount}).";
            return;
        }

        var topicName = SelectedTopicDetail.Topic.Name;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.ExpandPartitionsAsync(_session, topicName, _newPartitionCount);
            IsExpandingPartitions = false;
            await LoadTopicsAsync();
            await LoadDetailAsync(topicName);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "FullyQualifiedName~ExpandPartitions"`

Expected: PASS (2 tests).

- [ ] **Step 5: Run the full Features.Tests suite to check for regressions**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj`

Expected: PASS (all tests).

- [ ] **Step 6: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "feat(topics): add increase-partitions command"
```

---

### Task 4: TopicsViewModel — recreate topic with fewer partitions

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Modify: `src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: `ITopicService.RecreateTopicWithFewerPartitionsAsync(IKafkaSession, string, int, short)` from Task 1; same private members as Task 3.
- Produces: `bool IsRecreatingTopic`, `bool IsNotRecreatingTopic`, `int RecreatePartitionCount`, `string RecreateConfirmName`, `bool CanConfirmRecreate`, `ICommand ShowRecreateFormCommand`, `ICommand CancelRecreateCommand`, `ICommand RecreateTopicCommand`, `public Task RecreateTopicAsync()` — consumed by Task 6's AXAML bindings.

- [ ] **Step 1: Write the failing unit tests**

Add to `src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`, after `ExpandPartitionsAsync_rejects_count_not_greater_than_current`:

```csharp
    [Fact]
    public async Task RecreateTopicAsync_calls_service_when_confirmed_and_in_range()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(detail);
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1), Times.Once);
    }

    [Fact]
    public async Task RecreateTopicAsync_does_nothing_when_confirm_name_does_not_match()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(detail);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "wrong-name";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.RecreateTopicWithFewerPartitionsAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>()), Times.Never);
    }

    [Fact]
    public async Task RecreateTopicAsync_rejects_count_outside_valid_range()
    {
        var svc = new Mock<ITopicService>();
        var detail = new TopicDetail(new TopicInfo("orders", 4, 1),
            new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>())).ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders")).ReturnsAsync(detail);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 4;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.RecreateTopicWithFewerPartitionsAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>()), Times.Never);
        Assert.NotNull(vm.ErrorMessage);
    }
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "FullyQualifiedName~RecreateTopicAsync"`

Expected: build error — `TopicsViewModel` does not contain `RecreateConfirmName` / `RecreatePartitionCount` / `RecreateTopicAsync`.

- [ ] **Step 3: Add recreate state and command**

In `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, replace:

```csharp
    public bool IsExpandingPartitions { get => _isExpandingPartitions; private set { this.RaiseAndSetIfChanged(ref _isExpandingPartitions, value); this.RaisePropertyChanged(nameof(IsNotExpandingPartitions)); } }
    public bool IsNotExpandingPartitions => !_isExpandingPartitions;
    public int NewPartitionCount { get => _newPartitionCount; set => this.RaiseAndSetIfChanged(ref _newPartitionCount, value); }
```

with:

```csharp
    public bool IsExpandingPartitions { get => _isExpandingPartitions; private set { this.RaiseAndSetIfChanged(ref _isExpandingPartitions, value); this.RaisePropertyChanged(nameof(IsNotExpandingPartitions)); } }
    public bool IsNotExpandingPartitions => !_isExpandingPartitions;
    public int NewPartitionCount { get => _newPartitionCount; set => this.RaiseAndSetIfChanged(ref _newPartitionCount, value); }
    public bool IsRecreatingTopic { get => _isRecreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isRecreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotRecreatingTopic)); } }
    public bool IsNotRecreatingTopic => !_isRecreatingTopic;
    public int RecreatePartitionCount { get => _recreatePartitionCount; set => this.RaiseAndSetIfChanged(ref _recreatePartitionCount, value); }
    public string RecreateConfirmName
    {
        get => _recreateConfirmName;
        set
        {
            this.RaiseAndSetIfChanged(ref _recreateConfirmName, value);
            this.RaisePropertyChanged(nameof(CanConfirmRecreate));
        }
    }
    public bool CanConfirmRecreate => SelectedTopicDetail != null && _recreateConfirmName == SelectedTopicDetail.Topic.Name;
```

Replace the field block:

```csharp
    private bool _isExpandingPartitions;
    private int _newPartitionCount = 1;
```

with:

```csharp
    private bool _isExpandingPartitions;
    private int _newPartitionCount = 1;
    private bool _isRecreatingTopic;
    private int _recreatePartitionCount = 1;
    private string _recreateConfirmName = "";
```

Replace:

```csharp
    public ICommand ShowExpandFormCommand { get; }
    public ICommand CancelExpandCommand { get; }
    public ICommand ExpandPartitionsCommand { get; }
```

with:

```csharp
    public ICommand ShowExpandFormCommand { get; }
    public ICommand CancelExpandCommand { get; }
    public ICommand ExpandPartitionsCommand { get; }
    public ICommand ShowRecreateFormCommand { get; }
    public ICommand CancelRecreateCommand { get; }
    public ICommand RecreateTopicCommand { get; }
```

In the constructor, replace:

```csharp
        CancelExpandCommand = ReactiveCommand.Create(() => IsExpandingPartitions = false);
        ExpandPartitionsCommand = ReactiveCommand.CreateFromTask(ExpandPartitionsAsync);

        _ = LoadTopicsAsync();
    }
```

with:

```csharp
        CancelExpandCommand = ReactiveCommand.Create(() => IsExpandingPartitions = false);
        ExpandPartitionsCommand = ReactiveCommand.CreateFromTask(ExpandPartitionsAsync);
        ShowRecreateFormCommand = ReactiveCommand.Create(() =>
        {
            IsRecreatingTopic = true;
            RecreateConfirmName = "";
            RecreatePartitionCount = Math.Max(1, (SelectedTopicDetail?.Partitions.Count ?? 1) - 1);
        });
        CancelRecreateCommand = ReactiveCommand.Create(() => IsRecreatingTopic = false);
        RecreateTopicCommand = ReactiveCommand.CreateFromTask(RecreateTopicAsync);

        _ = LoadTopicsAsync();
    }

    public async Task RecreateTopicAsync()
    {
        if (SelectedTopicDetail == null || !CanConfirmRecreate) return;
        var currentCount = SelectedTopicDetail.Partitions.Count;
        if (_recreatePartitionCount < 1 || _recreatePartitionCount >= currentCount)
        {
            ErrorMessage = $"New partition count must be between 1 and {currentCount - 1}.";
            return;
        }

        var topicName = SelectedTopicDetail.Topic.Name;
        var replicationFactor = SelectedTopicDetail.Topic.ReplicationFactor;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.RecreateTopicWithFewerPartitionsAsync(_session, topicName, _recreatePartitionCount, replicationFactor);
            IsRecreatingTopic = false;
            await LoadTopicsAsync();
            await LoadDetailAsync(topicName);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj --filter "FullyQualifiedName~RecreateTopicAsync"`

Expected: PASS (3 tests).

- [ ] **Step 5: Run the full Features.Tests suite to check for regressions**

Run: `dotnet test src/Skat.KawkaProject.Features.Tests/Skat.KawkaProject.Features.Tests.csproj`

Expected: PASS (all tests).

- [ ] **Step 6: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "feat(topics): add recreate-with-fewer-partitions command"
```

---

### Task 5: TopicsView — increase partitions form

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`

**Interfaces:**
- Consumes: `IsExpandingPartitions`, `NewPartitionCount`, `ShowExpandFormCommand`, `CancelExpandCommand`, `ExpandPartitionsCommand` from Task 3.
- Produces: nothing consumed by later tasks (leaf UI change).

- [ ] **Step 1: Add the "Increase partitions" button to the detail panel's action bar**

In `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, replace:

```xml
                    <Border DockPanel.Dock="Bottom" Padding="8"
                            BorderBrush="{DynamicResource BorderSubtleBrush}"
                            BorderThickness="0,1,0,0">
                        <StackPanel Orientation="Horizontal" Spacing="6">
                            <Button Command="{Binding DeleteTopicCommand}"
                                    CommandParameter="{Binding SelectedTopic.Name}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource DestructiveBrush}"
                                    BorderBrush="{DynamicResource StatusErrorBrush}"
                                    Foreground="{DynamicResource DestructiveTextBrush}">
                                🗑 Delete
                            </Button>
                        </StackPanel>
                    </Border>
```

with:

```xml
                    <Border DockPanel.Dock="Bottom" Padding="8"
                            BorderBrush="{DynamicResource BorderSubtleBrush}"
                            BorderThickness="0,1,0,0">
                        <StackPanel Orientation="Horizontal" Spacing="6">
                            <Button Command="{Binding ShowExpandFormCommand}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource SurfaceBrush}"
                                    BorderBrush="{DynamicResource BorderBrush}">
                                ▲ Increase
                            </Button>
                            <Button Command="{Binding DeleteTopicCommand}"
                                    CommandParameter="{Binding SelectedTopic.Name}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource DestructiveBrush}"
                                    BorderBrush="{DynamicResource StatusErrorBrush}"
                                    Foreground="{DynamicResource DestructiveTextBrush}">
                                🗑 Delete
                            </Button>
                        </StackPanel>
                    </Border>
```

- [ ] **Step 2: Add the expand form, docked below the detail header**

Replace:

```xml
                    <!-- Detail header -->
                    <Border DockPanel.Dock="Top" Padding="12,8"
                            BorderBrush="{DynamicResource BorderSubtleBrush}"
                            BorderThickness="0,0,0,1">
                        <TextBlock Text="{Binding SelectedTopicDetail.Topic.Name}"
                                   FontSize="11" FontWeight="Bold"
                                   Foreground="{DynamicResource TextPrimaryBrush}" />
                    </Border>
```

with:

```xml
                    <!-- Detail header -->
                    <Border DockPanel.Dock="Top" Padding="12,8"
                            BorderBrush="{DynamicResource BorderSubtleBrush}"
                            BorderThickness="0,0,0,1">
                        <TextBlock Text="{Binding SelectedTopicDetail.Topic.Name}"
                                   FontSize="11" FontWeight="Bold"
                                   Foreground="{DynamicResource TextPrimaryBrush}" />
                    </Border>

                    <!-- Increase partitions form -->
                    <Border DockPanel.Dock="Top" Padding="10,8"
                            Background="{DynamicResource SurfaceBrush}"
                            BorderBrush="{DynamicResource AccentBrush}"
                            BorderThickness="0,0,0,1"
                            IsVisible="{Binding IsExpandingPartitions}">
                        <StackPanel Spacing="6">
                            <TextBlock Text="Increase partitions" FontSize="11" FontWeight="Bold"
                                       Foreground="{DynamicResource TextPrimaryBrush}" />
                            <StackPanel Orientation="Horizontal" Spacing="6">
                                <TextBlock FontSize="11" Foreground="{DynamicResource TextMutedBrush}"
                                           VerticalAlignment="Center">New count:</TextBlock>
                                <NumericUpDown Value="{Binding NewPartitionCount}" Minimum="1"
                                               Width="110" FontSize="11" />
                            </StackPanel>
                            <StackPanel Orientation="Horizontal" Spacing="6">
                                <Button Command="{Binding ExpandPartitionsCommand}" FontSize="11" Padding="8,4"
                                        Background="{DynamicResource AccentSubtleBrush}"
                                        BorderBrush="{DynamicResource AccentBrush}"
                                        Foreground="{DynamicResource AccentBrush}">Apply</Button>
                                <Button Command="{Binding CancelExpandCommand}" FontSize="11" Padding="8,4"
                                        Background="Transparent" BorderThickness="0"
                                        Foreground="{DynamicResource TextMutedBrush}">✕ Cancel</Button>
                            </StackPanel>
                        </StackPanel>
                    </Border>
```

- [ ] **Step 3: Build to verify the AXAML compiles**

Run: `dotnet build src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

Expected: Build succeeded, 0 errors (Avalonia's XAML compiler validates bindings against `TopicsViewModel` at build time via `x:DataType`).

- [ ] **Step 4: Manual smoke check**

Run: `dotnet run --project src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

Connect to a Kafka cluster, open Topics, select a topic, click "▲ Increase", verify the form appears with a `NumericUpDown` seeded to current+1, click Apply, verify the partition count updates in both the list and detail panel. Click "▲ Increase" then "✕ Cancel" and verify the form closes without side effects.

- [ ] **Step 5: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml
git commit -m "feat(topics): add increase-partitions form to Topics view"
```

---

### Task 6: TopicsView — recreate-with-fewer-partitions form

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`

**Interfaces:**
- Consumes: `IsRecreatingTopic`, `RecreatePartitionCount`, `RecreateConfirmName`, `CanConfirmRecreate`, `ShowRecreateFormCommand`, `CancelRecreateCommand`, `RecreateTopicCommand` from Task 4.
- Produces: nothing consumed by later tasks (leaf UI change).

- [ ] **Step 1: Add the "Recreate with fewer partitions" button to the action bar**

In `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, replace:

```xml
                            <Button Command="{Binding ShowExpandFormCommand}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource SurfaceBrush}"
                                    BorderBrush="{DynamicResource BorderBrush}">
                                ▲ Increase
                            </Button>
                            <Button Command="{Binding DeleteTopicCommand}"
                                    CommandParameter="{Binding SelectedTopic.Name}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource DestructiveBrush}"
                                    BorderBrush="{DynamicResource StatusErrorBrush}"
                                    Foreground="{DynamicResource DestructiveTextBrush}">
                                🗑 Delete
                            </Button>
```

with:

```xml
                            <Button Command="{Binding ShowExpandFormCommand}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource SurfaceBrush}"
                                    BorderBrush="{DynamicResource BorderBrush}">
                                ▲ Increase
                            </Button>
                            <Button Command="{Binding ShowRecreateFormCommand}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource SurfaceBrush}"
                                    BorderBrush="{DynamicResource StatusErrorBrush}"
                                    Foreground="{DynamicResource StatusErrorBrush}">
                                ⚠ Recreate
                            </Button>
                            <Button Command="{Binding DeleteTopicCommand}"
                                    CommandParameter="{Binding SelectedTopic.Name}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource DestructiveBrush}"
                                    BorderBrush="{DynamicResource StatusErrorBrush}"
                                    Foreground="{DynamicResource DestructiveTextBrush}">
                                🗑 Delete
                            </Button>
```

- [ ] **Step 2: Add the recreate form, docked below the action buttons**

Note the layout order after Task 5: detail header → increase-partitions form → action buttons → partition list. Insert the recreate form after the action buttons (whose content Step 1 of this task just changed), immediately before the partition list.

Replace (the closing tag of the action-buttons `Border`, identified by its `DeleteTopicCommand` button, followed by the partition list section):

```xml
                            <Button Command="{Binding DeleteTopicCommand}"
                                    CommandParameter="{Binding SelectedTopic.Name}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource DestructiveBrush}"
                                    BorderBrush="{DynamicResource StatusErrorBrush}"
                                    Foreground="{DynamicResource DestructiveTextBrush}">
                                🗑 Delete
                            </Button>
                        </StackPanel>
                    </Border>

                    <!-- Partition list -->
```

with:

```xml
                            <Button Command="{Binding DeleteTopicCommand}"
                                    CommandParameter="{Binding SelectedTopic.Name}"
                                    Padding="8,4" FontSize="11"
                                    Background="{DynamicResource DestructiveBrush}"
                                    BorderBrush="{DynamicResource StatusErrorBrush}"
                                    Foreground="{DynamicResource DestructiveTextBrush}">
                                🗑 Delete
                            </Button>
                        </StackPanel>
                    </Border>

                    <!-- Recreate topic form -->
                    <Border DockPanel.Dock="Top" Padding="10,8"
                            Background="{DynamicResource SurfaceBrush}"
                            BorderBrush="{DynamicResource StatusErrorBrush}"
                            BorderThickness="0,0,0,1"
                            IsVisible="{Binding IsRecreatingTopic}">
                        <StackPanel Spacing="6">
                            <TextBlock Text="⚠ Recreate with fewer partitions" FontSize="11" FontWeight="Bold"
                                       Foreground="{DynamicResource StatusErrorBrush}" />
                            <TextBlock Text="This deletes and recreates the topic. All messages in this topic will be permanently lost. This cannot be undone."
                                       FontSize="10" TextWrapping="Wrap"
                                       Foreground="{DynamicResource TextMutedBrush}" />
                            <StackPanel Orientation="Horizontal" Spacing="6">
                                <TextBlock FontSize="11" Foreground="{DynamicResource TextMutedBrush}"
                                           VerticalAlignment="Center">New count:</TextBlock>
                                <NumericUpDown Value="{Binding RecreatePartitionCount}" Minimum="1"
                                               Width="110" FontSize="11" />
                            </StackPanel>
                            <TextBox Text="{Binding RecreateConfirmName}"
                                     Watermark="Type the topic name to confirm"
                                     FontSize="11" Height="26" VerticalContentAlignment="Center" />
                            <StackPanel Orientation="Horizontal" Spacing="6">
                                <Button Command="{Binding RecreateTopicCommand}"
                                        IsEnabled="{Binding CanConfirmRecreate}"
                                        FontSize="11" Padding="8,4"
                                        Background="{DynamicResource DestructiveBrush}"
                                        BorderBrush="{DynamicResource StatusErrorBrush}"
                                        Foreground="{DynamicResource DestructiveTextBrush}">Recreate topic</Button>
                                <Button Command="{Binding CancelRecreateCommand}" FontSize="11" Padding="8,4"
                                        Background="Transparent" BorderThickness="0"
                                        Foreground="{DynamicResource TextMutedBrush}">✕ Cancel</Button>
                            </StackPanel>
                        </StackPanel>
                    </Border>

                    <!-- Partition list -->
```

- [ ] **Step 3: Build to verify the AXAML compiles**

Run: `dotnet build src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Manual smoke check**

Run: `dotnet run --project src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

Select a topic with 4+ partitions, click "⚠ Recreate", verify the warning text and that "Recreate topic" stays disabled until the typed name exactly matches the topic name, then confirm and verify the partition count drops and the topic's messages are gone (expected — this is destructive by design). Verify Cancel closes the form without calling the service.

- [ ] **Step 5: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml
git commit -m "feat(topics): add recreate-with-fewer-partitions form to Topics view"
```

---

### Task 7: TopicsView — "view messages" button per partition row

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`

**Interfaces:**
- Consumes: `ViewPartitionMessagesCommand` from Task 2, bound with `RelativeSource={RelativeSource AncestorType=UserControl}` since the `ItemsControl.ItemTemplate`'s `DataContext` is a `PartitionInfo`, not the `TopicsViewModel`.
- Produces: nothing (final task).

- [ ] **Step 1: Widen the partition columns and add a header cell**

In `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, replace:

```xml
                            <!-- Partition column headers -->
                            <Grid ColumnDefinitions="30,*,*" Margin="0,0,0,4">
                                <TextBlock Grid.Column="0" Text="#"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource TextFaintBrush}" />
                                <TextBlock Grid.Column="1" Text="EARLIEST"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource TextFaintBrush}" />
                                <TextBlock Grid.Column="2" Text="LATEST"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource TextFaintBrush}" />
                            </Grid>
```

with:

```xml
                            <!-- Partition column headers -->
                            <Grid ColumnDefinitions="30,*,*,30" Margin="0,0,0,4">
                                <TextBlock Grid.Column="0" Text="#"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource TextFaintBrush}" />
                                <TextBlock Grid.Column="1" Text="EARLIEST"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource TextFaintBrush}" />
                                <TextBlock Grid.Column="2" Text="LATEST"
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource TextFaintBrush}" />
                                <TextBlock Grid.Column="3" Text=""
                                           FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource TextFaintBrush}" />
                            </Grid>
```

- [ ] **Step 2: Add the view-messages button to each partition row**

Replace:

```xml
                            <ItemsControl ItemsSource="{Binding SelectedTopicDetail.Partitions}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid ColumnDefinitions="30,*,*" Margin="0,3">
                                            <TextBlock Grid.Column="0" Text="{Binding PartitionId}"
                                                       FontSize="11" FontWeight="SemiBold"
                                                       Foreground="{DynamicResource TextMutedBrush}" />
                                            <TextBlock Grid.Column="1" Text="{Binding EarliestOffset}"
                                                       FontSize="11"
                                                       FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                                       Foreground="{DynamicResource TextPrimaryBrush}" />
                                            <TextBlock Grid.Column="2" Text="{Binding LatestOffset}"
                                                       FontSize="11"
                                                       FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                                       Foreground="{DynamicResource StatusLiveBrush}" />
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
```

with:

```xml
                            <ItemsControl ItemsSource="{Binding SelectedTopicDetail.Partitions}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid ColumnDefinitions="30,*,*,30" Margin="0,3">
                                            <TextBlock Grid.Column="0" Text="{Binding PartitionId}"
                                                       FontSize="11" FontWeight="SemiBold"
                                                       Foreground="{DynamicResource TextMutedBrush}" />
                                            <TextBlock Grid.Column="1" Text="{Binding EarliestOffset}"
                                                       FontSize="11"
                                                       FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                                       Foreground="{DynamicResource TextPrimaryBrush}" />
                                            <TextBlock Grid.Column="2" Text="{Binding LatestOffset}"
                                                       FontSize="11"
                                                       FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                                       Foreground="{DynamicResource StatusLiveBrush}" />
                                            <Button Grid.Column="3" Content="👁"
                                                    Command="{Binding DataContext.ViewPartitionMessagesCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                                    CommandParameter="{Binding PartitionId}"
                                                    FontSize="11" Padding="4,0" MinWidth="0"
                                                    Background="Transparent" BorderThickness="0"
                                                    Foreground="{DynamicResource AccentBrush}"
                                                    ToolTip.Tip="View messages" />
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
```

- [ ] **Step 3: Build to verify the AXAML compiles**

Run: `dotnet build src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Manual smoke check**

Run: `dotnet run --project src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`

Select a topic, click the 👁 button on a partition row, verify the app navigates to the Messages view with that topic and partition pre-filled in Offset mode, and that messages load automatically without clicking Fetch.

- [ ] **Step 5: Run the full test suite one last time**

Run: `dotnet test src/Skat.KawkaProject.sln`

Expected: PASS (all unit + integration tests across the solution).

- [ ] **Step 6: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml
git commit -m "feat(topics): add view-messages button per partition row"
```
