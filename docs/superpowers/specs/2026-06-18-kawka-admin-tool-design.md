# Kawka Admin Tool — Design Spec

**Date:** 2026-06-18
**Status:** Approved

---

## Overview

Kawka is a cross-platform desktop Kafka administration tool built with Avalonia UI and Confluent.Kafka on .NET. This spec covers the evolution from the current single-screen producer into a full admin tool supporting connection profile management, topic management, message browsing, and cluster/broker monitoring.

Target use: both local development clusters and production clusters (with authentication).

---

## Architecture

The solution uses a compile-time modular structure — seven projects statically referenced, with clear dependency boundaries.

### Project Layout

```
Skat.KawkaProject.Core                   — models, interfaces, no external deps
Skat.KawkaProject.Kafka                  — Confluent.Kafka implementation of Core interfaces
Skat.KawkaProject.Features.Connections   — connection editor dialog (VM + View for create/edit profile)
Skat.KawkaProject.Features.Topics        — topic listing, create/delete, partition info (VM + View)
Skat.KawkaProject.Features.Messages      — message browsing & live tail (VM + View)
Skat.KawkaProject.Features.Cluster       — broker info, consumer groups, lag (VM + View)
Skat.KawkaProject.UI                     — shell, sidebar, navigation host, DI wiring
```

### Dependency Flow

- `Core` has no external dependencies — only interfaces and domain models
- `Kafka` implements `Core` interfaces using Confluent.Kafka
- Each `Features.*` project depends only on `Core` (not `Kafka`) — fully testable with mocks
- `Features.Connections` provides only the connection editor dialog (the form for creating/editing a profile); it does not host a main content-area view — the sidebar in `UI` handles connection tree rendering directly
- `UI` depends on all feature projects + `Kafka` and wires everything together via DI at startup

No feature ViewModel ever references `Confluent.Kafka` directly.

### Navigation

ReactiveUI's `RoutedViewHost` fills the main content area. The sidebar raises navigation commands; the shell router loads the matching feature view. The existing `Avalonia.ReactiveUI` package already supports this pattern.

---

## Core Interfaces & Models (`Skat.KawkaProject.Core`)

### Connection

**`ConnectionProfile`** — persisted connection descriptor:
- `Name` (string)
- `BootstrapServers` (string)
- `AuthType` (enum: None, SaslPlaintext, SaslSsl, Ssl)
- `SaslUsername`, `SaslPassword` (strings, used when AuthType is SASL)
- `SslCertificatePath`, `SslKeyPath`, `SslCaPath` (strings, used when AuthType is SSL)

> **Security note (v1):** Passwords are stored in plaintext in `profiles.json`. This is acceptable for a dev-focused tool at this stage; credential encryption is deferred to a future iteration.

**`IConnectionProfileRepository`** (Core interface, implemented in `Core`):
- CRUD for `ConnectionProfile` objects (persisted to `profiles.json`)

**`IKafkaConnectionFactory`** (Core interface, implemented in `Kafka`):
- `ConnectAsync(ConnectionProfile) → IKafkaSession` — opens a live Confluent.Kafka connection and returns the session handle

Splitting the two responsibilities keeps profile CRUD testable without a real Kafka broker.

**`IKafkaSession`** — opaque handle representing an active connection to a cluster, disposed on disconnect.

### Feature Services

**`ITopicService`**:
- `ListTopicsAsync(IKafkaSession) → IEnumerable<TopicInfo>`
- `GetTopicDetailAsync(IKafkaSession, topicName) → TopicDetail`
- `CreateTopicAsync(IKafkaSession, name, partitionCount, replicationFactor)`
- `DeleteTopicAsync(IKafkaSession, topicName)`
- `ExpandPartitionsAsync(IKafkaSession, topicName, newPartitionCount)`

**`IMessageService`**:
- `FetchMessagesAsync(IKafkaSession, topicName, partition, startOffset, count) → IEnumerable<KafkaMessage>`
- `TailAsync(IKafkaSession, topicName) → IObservable<KafkaMessage>` — hot observable, caller disposes to unsubscribe

**`IClusterService`**:
- `ListBrokersAsync(IKafkaSession) → IEnumerable<BrokerInfo>`
- `ListConsumerGroupsAsync(IKafkaSession) → IEnumerable<ConsumerGroupInfo>`
- `GetGroupLagAsync(IKafkaSession, groupId) → IEnumerable<PartitionLag>`

### Shared Models

| Model | Key Fields |
|---|---|
| `TopicInfo` | Name, PartitionCount, ReplicationFactor |
| `TopicDetail` | TopicInfo + list of PartitionInfo |
| `PartitionInfo` | PartitionId, LeaderBrokerId, EarliestOffset, LatestOffset |
| `KafkaMessage` | Topic, Partition, Offset, Key, Value, Timestamp |
| `BrokerInfo` | BrokerId, Host, Port, IsController |
| `ConsumerGroupInfo` | GroupId, State, MemberCount |
| `PartitionLag` | Topic, Partition, CurrentOffset, EndOffset, Lag |

---

## Shell & Sidebar Navigation (`Skat.KawkaProject.UI`)

### Layout

Two-panel window: fixed-width sidebar on the left, `RoutedViewHost` content area on the right.

### Sidebar Tree

```
[+] Add Connection
────────────────────
▶ Production Cluster    ● (connected)
    Topics
    Messages
    Cluster Info
▶ Local Dev             ○ (disconnected)
    Topics
    Messages
    Cluster Info
```

- Each saved `ConnectionProfile` appears as a collapsible tree node
- A status indicator (colored dot) shows: connecting / connected / error per node
- Clicking a child item (Topics, Messages, Cluster Info) navigates the content area to that feature's ViewModel, passing the active `IKafkaSession`
- Clicking a disconnected connection node triggers `ConnectAsync`; errors appear as a tooltip on the indicator

### Session Lifecycle

- Multiple sessions can be open simultaneously — switching between tree nodes does not disconnect
- Sessions are disposed when the user explicitly disconnects a node or the app closes
- If an active session drops mid-use, the content area shows a reconnect prompt and the sidebar indicator turns red

---

## Feature Modules

### Topics (`Features.Topics`)

**List view:** Searchable, filterable table with columns: Name, Partitions, Replication Factor. Filter is client-side over the loaded topic list.

**Detail panel:** Appears below or beside the list on row selection. Shows per-partition table: Partition ID, Leader Broker, Earliest Offset, Latest Offset.

**Toolbar actions:**
- *Create Topic* — dialog collects name, partition count, replication factor
- *Delete Topic* — requires explicit confirmation dialog before executing
- *Expand Partitions* — separate explicit action (not inline edit); Kafka does not support reducing partition count, so this is never offered as a decrease

### Messages (`Features.Messages`)

**Mode toggle (toolbar button):**

*Offset Mode:*
- User selects partition and start offset (or a timestamp that resolves to an offset)
- Fetch N messages (configurable, default 50)
- Page forward / backward through results

*Tail Mode:*
- Subscribes from latest offset; new messages prepend to the top of the list
- Pause button suspends display updates without unsubscribing the observable
- Resume resumes display

**Message list columns:** Partition, Offset, Timestamp, Key, Value (truncated).

**Detail pane:** Clicking a row opens a detail pane with the full message value. If the value parses as valid JSON it is pretty-printed; otherwise shown as raw text.

**Client-side filter bar:** Searches within currently loaded messages only (not a Kafka-level filter).

### Cluster Info (`Features.Cluster`)

Three tabs:

- *Brokers* — table: Broker ID, Host, Port, Controller (boolean flag)
- *Consumer Groups* — table: Group ID, State, Member Count
- *Lag* — user selects a consumer group from a dropdown; table shows Topic, Partition, Current Offset, End Offset, Lag. Auto-refreshes every 10 seconds; manual refresh button also available.

---

## Error Handling

### Connection Errors

Shown inline on the sidebar node (red indicator + hover tooltip with exception message). The content area shows a reconnect prompt if the active session drops.

### Operation Errors

Each feature ViewModel exposes an `ErrorMessage` property bound to a dismissible banner at the top of its view. Raw Confluent.Kafka exception messages are included in a collapsible "details" section within the banner for debugging.

### Async Operations

All Kafka calls are async. ViewModels expose `IsBusy` boolean properties bound to loading indicators. Live tail results are marshalled back to the UI thread via `ObserveOn(RxApp.MainThreadScheduler)`.

### Connection Profile Persistence

Profiles stored as JSON at `Environment.SpecialFolder.ApplicationData/KawkaProject/profiles.json`. Malformed entries are logged as warnings and skipped — the app always launches successfully even with a corrupt profile file.

---

## Testing Strategy

- **Unit tests:** Feature ViewModels are unit-tested by injecting mock implementations of `ITopicService`, `IMessageService`, and `IClusterService`. `Core` interfaces are the primary seam.
- **Integration tests:** The `Kafka` project is tested against a real broker. Testcontainers for .NET spins up a Kafka container per test run.
- **No UI automation tests** at this stage — ViewModel logic covers the meaningful behavior.
