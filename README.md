# kawka

**kawka** (Polish: *little coffee*) is a cross-platform desktop Kafka admin tool built with .NET 10 and Avalonia.

## Origin

The name and the idea came from the same place: a café in Kraków.

After spending too much time hunting for a Kafka admin tool that actually worked — equally well on Windows and on Linux, without needing a browser, a JVM, or fifteen configuration files — the frustration hit a peak over a cup of coffee with a colleague. Every tool available either ran poorly on one OS, required a heavy runtime, or had an interface that felt designed to hide information rather than show it. The conversation went something like: *"why doesn't something simple like this just exist?"*

It didn't. So we built it.

*Kawka* — the diminutive of *kawa* ("coffee") in Polish — felt like the right name. Small, warm, and designed to get things done without fuss.

## Features

- Manage multiple Kafka connections with persistent profiles (no-auth, SASL Plaintext, SASL SSL, mutual TLS)
- **Topics** — list, filter, create (name / partitions / replication factor), delete with confirmation, inspect partition offsets, grow the partition count, and shrink it by recreating the topic
- **Messages** — fetch by partition/offset range, live tail, produce (with topic autocomplete), client-side text filter, JSON pretty-print, key display
- **Cluster** — broker list, consumer groups, partition lag per group
- Dark / Light theme toggle
- Profiles stored locally in JSON (no external database)

---

## Architecture

### Solution layout

```
src/
├── Skat.KawkaProject.Core/                # Domain models & interfaces (no external deps)
│   ├── Interfaces/
│   │   ├── IKafkaConnectionFactory.cs
│   │   ├── IKafkaSession.cs
│   │   ├── IConnectionProfileRepository.cs
│   │   ├── ITopicService.cs
│   │   ├── IMessageService.cs
│   │   └── IClusterService.cs
│   ├── Models/
│   │   ├── ConnectionProfile.cs           # Auth config (AuthType: None/SaslPlaintext/SaslSsl/Ssl)
│   │   ├── TopicInfo.cs / TopicDetail.cs
│   │   ├── KafkaMessage.cs
│   │   ├── DestructiveAction.cs           # Canonical "what a destructive op loses / keeps"
│   │   └── BrokerInfo.cs / PartitionInfo.cs / PartitionLag.cs / ConsumerGroupInfo.cs
│   └── Exceptions/
│       └── TopicRecreateFailedException.cs  # Carries the failed stage + everything needed to rebuild
│
├── Skat.KawkaProject.Kafka/               # Confluent.Kafka implementations of Core interfaces
│   ├── KafkaConnectionFactory.cs          # Opens a session (validates connectivity via GetMetadata)
│   ├── KafkaSession.cs                    # Holds bootstrap + auth; applies to producer/consumer configs
│   ├── TopicService.cs                    # Thin adapter over AdminClient
│   ├── TopicRecreateOperation.cs          # Delete-and-recreate saga (kept out of the adapter)
│   ├── TopicMetadataFacts.cs              # Pure derivations + the guards that refuse a bad recreate
│   ├── KafkaTimeouts.cs
│   ├── MessageService.cs                  # Fetch (Task.Run wrapper), Tail (IObservable), Produce
│   ├── ClusterService.cs
│   └── ConnectionProfileRepository.cs    # JSON persistence to OS app-data folder
│
├── Skat.KawkaProject.Features.Connections/
├── Skat.KawkaProject.Features.Topics/
├── Skat.KawkaProject.Features.Messages/
├── Skat.KawkaProject.Features.Cluster/
│
├── Skat.KawkaProject.UI/                  # App entry point, DI, navigation shell
│   ├── App.axaml.cs                       # ServiceCollection wiring + Splat view registration
│   ├── Views/
│   │   ├── MainWindow.axaml               # Header + sidebar + RoutedViewHost
│   │   ├── SidebarView.axaml              # Connection list, navigate to features
│   │   └── LogoMark.axaml                 # Vector coffee-cup logo (pure AXAML, no image dep)
│   ├── ViewModels/
│   │   ├── ShellViewModel.cs              # ReactiveUI IScreen, owns RoutingState
│   │   └── SidebarViewModel.cs / ConnectionNodeViewModel.cs
│   └── Assets/Themes/
│       ├── DarkTheme.axaml                # Catppuccin-inspired dark palette
│       └── LightTheme.axaml               # Catppuccin Latte light palette
│
├── Skat.KawkaProject.Core.Tests/
├── Skat.KawkaProject.Features.Tests/
└── Skat.KawkaProject.Kafka.Tests/
```

### Key patterns

| Concern | Approach |
|---|---|
| UI framework | Avalonia 11 with compiled AXAML bindings (`x:DataType`) |
| MVVM | ReactiveUI (`ReactiveObject`, `ReactiveCommand`, `IRoutableViewModel`) |
| Navigation | `RoutingState` + `RoutedViewHost`; views resolved via Splat locator (`IViewFor<TViewModel>`) |
| Confirmation dialogs | `Interaction<TInput, TOutput>` on ViewModel; handler registered in View's `WhenActivated` |
| Async Kafka calls | `consumer.Consume()` is blocking — always wrapped in `Task.Run` to keep UI thread free |
| Destructive operations | The saga owns the facts: it re-reads live metadata and refuses rather than trusting a value the UI passed in. Warning text comes from `DestructiveAction`, one list shared by every frontend |
| Live tail | `IObservable<KafkaMessage>` backed by a background `Task.Run` loop, observed on `RxApp.MainThreadScheduler` |
| Dependency injection | `Microsoft.Extensions.DependencyInjection`; services wired in `App.axaml.cs` |
| Profile persistence | `ConnectionProfileRepository` → `profiles.json` in the OS application-data folder |

### Data flow for a fetch

```
User clicks Fetch
  └─► FetchCommand (ReactiveCommand.CreateFromTask)
        └─► FetchMessagesAsync() [UI thread]
              └─► Task.Run(() => { consumer.Consume() loop })   [thread pool]
                    └─► returns IEnumerable<KafkaMessage>
                          └─► Messages.Clear() + Add()          [back on UI thread via await]
```

---

## Changing a topic's partition count

Kafka treats the two directions very differently, and so does kawka.

**Growing** is a normal admin call. Existing records stay where they are, but the key-to-partition mapping changes for everything written afterwards — so records sharing a key can land in a different partition than their predecessors, and per-key ordering is not preserved across the change.

**Shrinking is not something Kafka can do.** There is no in-place operation, so kawka deletes the topic and creates it again with fewer partitions. That is destructive and irreversible:

| | |
|---|---|
| Destroyed | every message in the topic; committed consumer group offsets — depending on `auto.offset.reset`, consumers may then silently skip or replay records |
| Preserved | topic-level config overrides, read back and reapplied by kawka |
| Unaffected | literal ACLs bound to the same topic name — they survive, so there is nothing to re-grant |

Because the operation cannot be undone, it is gated:

- You must type the topic name to arm the button, and the whole UI stays disabled until the operation settles.
- The replication factor is **derived from the live topic**, never from what the screen happened to be showing — a reassignment that completed since the panel loaded cannot silently change durability. A non-uniform assignment is flattened to its weakest partition.
- Before anything is deleted, kawka refuses the operation outright if the cluster cannot answer for the topic consistently: a partition reporting an error, a replica list that came back empty, or two metadata reads that disagree with each other. A number accepted here would only be rejected by the broker *after* the delete, leaving the topic gone and nothing in its place.
- If the recreate does fail after the delete, the error message carries everything needed to rebuild the topic by hand — partition count, replication factor and config overrides — because at that point the topic list can no longer answer "what was it?".

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0 or later |
| Kafka broker | any reachable instance (local Docker works) |

No other global tools are required.

---

## Setup

```bash
git clone <repo-url>
cd kawka_project/src
dotnet restore
```

---

## Running

### Windows

```powershell
cd src
dotnet run --project Skat.KawkaProject.UI
```

Or open `src/Skat.KawkaProject.sln` in **Rider** or **Visual Studio 2022** and press Run.

> **WSL2 note** — if you develop in WSL2 and also build on Windows, the Linux `obj/Debug` and `bin/Debug` artifacts can corrupt the Windows IDE build. Clean them before switching back to Windows:
>
> ```bash
> # run inside WSL2
> find /path/to/kawka_project/src -type d \( -name "obj" -o -name "bin" \) | xargs rm -rf
> ```

### Linux

```bash
cd src
dotnet run --project Skat.KawkaProject.UI
```

Avalonia renders natively on Linux via X11 or Wayland. No additional packages are needed beyond the .NET SDK on most distributions. If you see font or rendering issues on a minimal install, ensure `libfontconfig` is present:

```bash
# Debian / Ubuntu
sudo apt-get install -y libfontconfig1

# Fedora / RHEL
sudo dnf install -y fontconfig
```

### Release / self-contained build

```bash
# Windows
dotnet publish Skat.KawkaProject.UI -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -o ./publish

# Linux
dotnet publish Skat.KawkaProject.UI -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true -o ./publish

# macOS (Apple Silicon)
dotnet publish Skat.KawkaProject.UI -c Release -r osx-arm64 \
  --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

## Running tests

```bash
cd src
dotnet test
```

Tests use **xUnit**, with **Moq** in the ViewModel and Core suites.

| Suite | Needs | Notes |
|---|---|---|
| `Skat.KawkaProject.Core.Tests` | nothing | Pure domain logic |
| `Skat.KawkaProject.Features.Tests` | nothing | ViewModels in-process against mocked services |
| `Skat.KawkaProject.Kafka.Tests` | **Docker running** | Spins up its own broker per class via `Testcontainers.Kafka` — you do *not* need to point it at an existing cluster |

Without Docker the Kafka suite fails to start rather than skipping, so run `docker info` first if it errors out. Expect the Kafka suite to take a few minutes: each test class starts a real broker container.

Some behaviour cannot be reached through a healthy single-broker container at all — a partition reporting an error, a replica list the broker omitted, two metadata reads disagreeing. Those guards are covered by unit tests over pure functions (`TopicFactsTests`, `ReplicationFactorTests`) precisely because the integration suite would stay green if they were removed.

---

## Local Kafka with Docker

The quickest way to get a broker running locally (KRaft mode, no ZooKeeper):

```bash
docker run -d \
  --name kawka-kafka \
  -p 9092:9092 \
  -e KAFKA_NODE_ID=1 \
  -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://:9092,CONTROLLER://:9093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093 \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 \
  -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 \
  -e KAFKA_LOG_DIRS=/tmp/kraft-combined-logs \
  -e CLUSTER_ID=MkU3OEVBNTcwNTJENDM2Qk \
  apache/kafka:latest
```

Then add a connection in Kawka with **Bootstrap servers**: `localhost:9092` and **Auth**: None.

> Make sure the container exposes the port with `-p 9092:9092`. Running without `-p` will cause a "broker transport failure" even if the container is up.

---

## Connection profiles

Profiles are saved automatically to:

| OS | Path |
|---|---|
| Windows | `%APPDATA%\KawkaProject\profiles.json` |
| Linux / macOS | `~/.config/KawkaProject/profiles.json` |

The file is plain JSON and can be edited manually or deleted to reset all connections.
