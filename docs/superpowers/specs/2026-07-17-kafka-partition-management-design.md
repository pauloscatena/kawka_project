# Kafka Partition Management — Design

## Goal

Extend the Topics feature so users can manage partitions beyond the current
read-only view: increase a topic's partition count, "shrink" partitions by
recreating the topic (Kafka has no native partition-delete), and jump
straight from a partition row to that partition's messages.

## Context / Kafka constraint

Kafka does not support deleting or reducing the partition count of an
existing topic — `CreatePartitions` can only increase it. There is no
`RemovePartitions` API. To end up with fewer partitions, the only path is
delete the topic and recreate it with the desired count, which destroys all
messages currently in the topic. The design below treats "increase" and
"reduce (recreate)" as two distinct, clearly labeled operations rather than
pretending Kafka has a `DeletePartition` primitive.

## 1. Increase partitions

Already backed by `ITopicService.ExpandPartitionsAsync` (existing method,
unused by any ViewModel/View today). No Core/Kafka changes needed — only
ViewModel + View wiring.

**`TopicsViewModel` additions:**
- `bool IsExpandingPartitions` / `IsNotExpandingPartitions`
- `int NewPartitionCount`
- `ICommand ShowExpandFormCommand` — opens the form, seeds
  `NewPartitionCount` to `SelectedTopicDetail.Partitions.Count + 1`
- `ICommand CancelExpandCommand`
- `ICommand ExpandPartitionsCommand` — calls
  `_topicService.ExpandPartitionsAsync(session, topicName, NewPartitionCount)`,
  then reloads the selected topic's detail (`LoadDetailAsync`) and the topic
  list (partition count changed). Standard `IsBusy`/`ErrorMessage` handling
  matching `CreateTopicAsync`.

**View:** new inline form in `TopicsView.axaml`, same visual pattern as the
"New Topic" form, triggered by a "▲ Increase partitions" button in the
detail panel's action bar (next to "🗑 Delete"). `NumericUpDown` for the new
count with `Minimum` bound to current partition count + 1 (set imperatively
when the form opens, since Avalonia bindings for `Minimum` would need a
converter — simplest to just set the seed value and rely on validation: if
`NewPartitionCount <= current`, `ExpandPartitionsCommand` shows an
`ErrorMessage` and does not call the service).

## 2. Reduce partitions via recreate

**`Core` model:** no new model type needed — configs are passed as
`IReadOnlyDictionary<string, string>`.

**`ITopicService` additions:**
```csharp
Task<IReadOnlyDictionary<string, string>> GetTopicConfigAsync(IKafkaSession session, string topicName);
Task RecreateTopicWithFewerPartitionsAsync(IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor);
```

**`TopicService` implementation:**
- `GetTopicConfigAsync`: `admin.DescribeConfigsAsync` on a
  `ConfigResource { Type = ResourceType.Topic, Name = topicName }`; return
  entries where `IsDefault == false` as a plain dictionary (only
  explicitly-set overrides are preserved — matches the "preserve all
  configs" requirement without dragging in Kafka's full default-config
  surface, which isn't meaningful to copy to a new topic anyway).
- `RecreateTopicWithFewerPartitionsAsync`:
  1. `var config = await GetTopicConfigAsync(...)`
  2. `await admin.DeleteTopicsAsync(new[] { topicName })`
  3. Poll `admin.GetMetadata(topicName, ...)` (short interval, e.g. 300ms)
     until the topic is absent from `meta.Topics` or a topic error confirms
     it's gone, with an overall timeout (~30s) — `DeleteTopicsAsync`
     completes when the controller acknowledges the request, not when the
     topic is fully purged from broker metadata, so recreating immediately
     can race and fail with "topic already exists."
  4. `await admin.CreateTopicsAsync(new[] { new TopicSpecification { Name = topicName, NumPartitions = newPartitionCount, ReplicationFactor = replicationFactor, Configs = config.ToDictionary(...) } })`
  5. On timeout waiting for deletion, throw a clear exception
     ("Timed out waiting for topic deletion before recreate") so the
     ViewModel surfaces it via `ErrorMessage`.

**`TopicsViewModel` additions:**
- `bool IsRecreatingTopic` / `IsNotRecreatingTopic`
- `int RecreatePartitionCount`
- `string RecreateConfirmName` (text the user types to confirm)
- `bool CanConfirmRecreate => RecreateConfirmName == SelectedTopicDetail?.Topic.Name`
- `ICommand ShowRecreateFormCommand` — seeds `RecreatePartitionCount` to
  `current - 1` (only enabled/shown when current partition count > 1),
  clears `RecreateConfirmName`
- `ICommand CancelRecreateCommand`
- `ICommand RecreateTopicCommand` — guarded by `CanConfirmRecreate` and
  `RecreatePartitionCount` in `1..(current - 1)`; calls
  `_topicService.RecreateTopicWithFewerPartitionsAsync(session, topicName, RecreatePartitionCount, currentReplicationFactor)`,
  then reloads detail + topic list. Standard busy/error handling.

**View:** inline form (same family as create/expand), triggered by
"⚠ Recreate with fewer partitions" in the detail panel's action bar. Content:
- Warning text: "This deletes and recreates the topic. **All messages in
  this topic will be permanently lost.** This cannot be undone."
- `NumericUpDown` for new partition count (`Minimum=1`,
  `Maximum=current-1`)
- `TextBox` bound to `RecreateConfirmName`, watermark "Type the topic name
  to confirm"
- Confirm button bound to `RecreateTopicCommand`, `IsEnabled` via
  `CanConfirmRecreate`, styled with the destructive brushes (same as the
  existing Delete button)
- Cancel button

No new `Interaction<,>` needed — the type-to-confirm text field is a
sufficient, simpler gate than reusing the `Window`-based
`ConfirmDelete`-style dialog, and keeps the destructive action's context
(topic name, partition count) visible while confirming.

## 3. View messages per partition

**`TopicsViewModel` constructor** gains an `IMessageService messageService`
parameter (stored as `_messageService`), threaded through from
`ConnectionNodeViewModel.NavigateToTopicsCommand`, which already has
`messageService` injected.

**New command:**
```csharp
public ICommand ViewPartitionMessagesCommand { get; }
```
```csharp
ViewPartitionMessagesCommand = ReactiveCommand.Create<int>(partition =>
{
    if (SelectedTopicDetail == null) return;
    var messagesVm = new MessagesViewModel(HostScreen, _session, _messageService, _topicService)
    {
        TopicName = SelectedTopicDetail.Topic.Name,
        Partition = partition,
        Mode = MessageMode.Offset,
    };
    HostScreen.Router.Navigate.Execute(messagesVm);
    _ = messagesVm.FetchMessagesAsync();
});
```
(`_session` needs to be reachable — it already is, it's the field
`TopicsViewModel` stores today.)

**View:** in the partition `ItemsControl.ItemTemplate` in `TopicsView.axaml`,
add a 4th column with a small "👁" button:
```xml
<Button Grid.Column="3" Content="👁"
        Command="{Binding DataContext.ViewPartitionMessagesCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
        CommandParameter="{Binding PartitionId}"
        FontSize="11" Padding="4,2" .../>
```
Adjust the partition header `Grid.ColumnDefinitions` from `30,*,*` to
`30,*,*,30` and add a matching blank header cell.

## Testing

- `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`: add cases
  for `GetTopicConfigAsync` (returns overridden configs, e.g. set
  `retention.ms` at creation and confirm it round-trips) and
  `RecreateTopicWithFewerPartitionsAsync` (create topic with N partitions +
  a custom config, recreate with N-1, assert new partition count and that
  the custom config value survived).
- `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`: add cases for
  `ExpandPartitionsCommand` (happy path + rejects count <= current),
  `RecreateTopicCommand` (gated by `CanConfirmRecreate`, rejects count
  outside range), and `ViewPartitionMessagesCommand` (navigates and presets
  `TopicName`/`Partition` on the pushed `MessagesViewModel`).

## Out of scope

- Preserving/copying messages during a partition-reduce recreate (Kafka
  offers no server-side way to do this; a client-side copy would need to
  consume and reproduce every message, which is a much larger feature and
  wasn't requested).
- Per-partition config overrides (Kafka partitions don't have independent
  configs — config is topic-level).
- Confirmation dialog reuse/refactor of the existing `ConfirmDelete`
  `Interaction<,>` — the type-to-confirm text field is simpler and
  sufficient for this destructive action.
