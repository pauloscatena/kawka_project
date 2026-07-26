# Headless Text Interface (TUI) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar `Skat.KawkaProject.Tui`, um executável console que expõe as operações do Kawka em Linux sem modo gráfico, com REPL interativo estilo Claude Code e modo one-shot para script.

**Architecture:** Quatro camadas num projeto novo que referencia apenas `Core` e `Kafka`. A regra que sustenta o desenho: **comandos devolvem dados (`CommandResult`), não pixels** — renderização é outra camada, então quase toda a suíte de testes roda sem terminal. Uma única fronteira captura `Exception`: o dispatcher.

**Tech Stack:** .NET 10, Spectre.Console 0.57.2, xUnit 2.9.3 + Moq 4.20.72, Testcontainers.Kafka 4.4.0 (integração).

**Spec:** `docs/superpowers/specs/2026-07-24-tui-headless-design.md`

## Global Constraints

- Target framework `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` — igual aos projetos existentes.
- `Skat.KawkaProject.Tui` referencia **somente** `Skat.KawkaProject.Core` e `Skat.KawkaProject.Kafka`. Nunca `Features.*` nem `UI`.
- Nenhum projeto existente ganha referência nova. `Spectre.Console` só entra no projeto TUI e no seu projeto de testes.
- Composição por `Microsoft.Extensions.DependencyInjection`, espelhando `Skat.KawkaProject.UI/App.axaml.cs:32-39`: `IConnectionProfileRepository` e `IKafkaConnectionFactory` como singleton, `ITopicService`/`IMessageService`/`IClusterService` como transient.
- Nenhuma classe fora de `Rendering/`, `Input/` e `Safety/` pode referenciar `System.Console` ou `AnsiConsole`. Comandos devolvem `CommandResult`.
- Todos os comandos rodados a partir de `/mnt/d/dev/Skat/kawka_project/src`.
- **Sequenciamento:** as Fases 1-3 não têm dependência externa. A **Fase 4 exige que as Tasks 1-4 e a Task 9 do plano `2026-07-24-topic-recreate-hardening.md` estejam concluídas.** Tasks 1-4 entregam a validação no serviço e a `TopicRecreateFailedException`; a Task 9 entrega os renames (`DeleteAndRecreateTopicAsync`, `GetTopicConfigOverridesAsync`) que o código da Fase 4 chama pelo nome. Na prática, concluir as Fases 1-3 do plano de hardening cobre tudo isso. Não comece a Fase 4 antes.

## HARD GATE — revisão obrigatória entre tasks

**Nenhuma task começa antes de a anterior ser revisada pelo agente `qa-tester` e os bugs apontados serem corrigidos.**

Ordem obrigatória, ao fim de cada task deste plano:

1. Implementar a task e rodar os testes indicados nos seus steps.
2. Despachar o agente `qa-tester` sobre o que foi entregue naquela task.
3. Corrigir todo bug apontado.
4. Só então iniciar a próxima task.

**Única exceção:** um achado pode ser dispensado sem correção quando for demonstrado ser **falso positivo** (o problema relatado não existe no código) ou **falso negativo** (o resultado do teste não reflete o comportamento real) — nos dois casos, o resultado do teste não corresponde à realidade. A demonstração precisa citar o trecho de código ou a saída de execução que prova a divergência, registrada na resposta. *"Parece um falso positivo"* não basta.

Não pule o gate porque a task parece pequena ou porque a suíte já está verde. Se o `qa-tester` não puder rodar, pare e reporte — não prossiga assumindo que passaria.

Nota específica deste plano: o `qa-tester` usa Chrome para validar UI web, o que **não se aplica** a um executável de terminal. Aqui ele atua sobre execução real de comandos (`dotnet run`) e sobre a suíte de testes — reproduzindo os cenários e documentando divergências, não navegando.

## Correções da spec descobertas ao detalhar

Registrado aqui porque a spec afirma o contrário e o plano é a fonte da verdade da implementação:

1. **`produce` não aceita `--partition`.** `IMessageService.ProduceAsync(IKafkaSession, string topicName, string? key, string value)` não tem parâmetro de partição — a partição é escolhida pelo particionador do Kafka. A flag foi removida da superfície de comandos. Adicioná-la exigiria mudar `IMessageService`, o que está fora do escopo desta v1.
2. **`consume` exige partição.** `FetchMessagesAsync(session, topic, int partition, long startOffset, int count)` tem partição obrigatória; `--partition` passa a ter default `0` em vez de ser opcional-sem-default.
3. **`IConnectionProfileRepository.GetAll()` é síncrono**, então `profiles` não é `async` de verdade — devolve `Task.FromResult` para satisfazer a interface `ITuiCommand`.

---

## Estrutura de arquivos

```
Skat.KawkaProject.Tui/
  Skat.KawkaProject.Tui.csproj
  Program.cs                 — entry: argv → one-shot ou REPL; composição DI
  TuiHost.cs                 — loop REPL: prompt, dispatch, Ctrl+C
  ExitCodes.cs               — constantes de código de saída
  Commands/
    ITuiCommand.cs           — contrato de comando
    CommandContext.cs        — args, flags, sessão, confirmador
    CommandResult.cs         — Table | Pairs | Text | Failure
    ParsedCommand.cs         — verbo + args + flags
    ArgumentParser.cs        — string/argv → ParsedCommand
    CommandRegistry.cs       — nome → handler; gera help
    CommandDispatcher.cs     — ÚNICA fronteira que captura Exception
    ConnectionCommands.cs    — profiles, connect, disconnect, status
    TopicCommands.cs         — topics, describe
    ClusterCommands.cs       — brokers, groups, lag
    MessageCommands.cs       — consume, produce
    TopicAdminCommands.cs    — create, delete, increase, recreate
  Rendering/
    IResultRenderer.cs
    SpectreRenderer.cs       — TTY: tabelas e cores
    PlainTextRenderer.cs     — sem TTY: TSV
  Input/
    IKeySource.cs            — abstração de leitura de teclas
    ConsoleKeySource.cs
    LineHistory.cs
    PromptReader.cs          — caixa com borda, setas, histórico
  Safety/
    IConfirmer.cs
    InteractiveConfirmer.cs
    NonInteractiveConfirmer.cs

Skat.KawkaProject.Tui.Tests/
  Skat.KawkaProject.Tui.Tests.csproj
  ArgumentParserTests.cs  RendererTests.cs  DispatcherTests.cs
  ConnectionCommandsTests.cs  TopicCommandsTests.cs
  ClusterCommandsTests.cs  MessageCommandsTests.cs
  ConfirmerTests.cs  TopicAdminCommandsTests.cs
  PromptReaderTests.cs  LineHistoryTests.cs
```

---

# FASE 1 — Esqueleto e leitura

Ao fim desta fase a ferramenta já é útil por SSH: conecta, lista e descreve tópicos, em REPL e one-shot.

### Task 1: Projeto, tipos base e parser de argumentos

**Files:**
- Create: `Skat.KawkaProject.Tui/Skat.KawkaProject.Tui.csproj`
- Create: `Skat.KawkaProject.Tui/ExitCodes.cs`
- Create: `Skat.KawkaProject.Tui/Commands/CommandResult.cs`
- Create: `Skat.KawkaProject.Tui/Commands/ParsedCommand.cs`
- Create: `Skat.KawkaProject.Tui/Commands/ArgumentParser.cs`
- Create: `Skat.KawkaProject.Tui.Tests/Skat.KawkaProject.Tui.Tests.csproj`
- Test: `Skat.KawkaProject.Tui.Tests/ArgumentParserTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `CommandResult` (`Table`/`Pairs`/`Text`/`Failure`), `ParsedCommand(string Verb, IReadOnlyList<string> Args, IReadOnlyDictionary<string,string?> Flags)`, `ArgumentParser.ParseLine(string)`, `ArgumentParser.ParseArgv(IReadOnlyList<string>)`, `ExitCodes`. Todas as tasks seguintes consomem estes tipos.

- [x] **Step 1: Criar os dois projetos e registrá-los na solution**

```bash
cd /mnt/d/dev/Skat/kawka_project/src
dotnet new console -n Skat.KawkaProject.Tui -f net10.0
dotnet new xunit -n Skat.KawkaProject.Tui.Tests -f net10.0
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Tui/Skat.KawkaProject.Tui.csproj
dotnet sln Skat.KawkaProject.sln add Skat.KawkaProject.Tui.Tests/Skat.KawkaProject.Tui.Tests.csproj
dotnet add Skat.KawkaProject.Tui reference Skat.KawkaProject.Core Skat.KawkaProject.Kafka
dotnet add Skat.KawkaProject.Tui package Spectre.Console --version 0.57.2
dotnet add Skat.KawkaProject.Tui package Microsoft.Extensions.DependencyInjection --version 9.0.6
dotnet add Skat.KawkaProject.Tui.Tests reference Skat.KawkaProject.Tui
dotnet add Skat.KawkaProject.Tui.Tests package Moq --version 4.20.72
dotnet add Skat.KawkaProject.Tui.Tests package Spectre.Console --version 0.57.2
rm Skat.KawkaProject.Tui/Program.cs
```

- [x] **Step 2: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/ArgumentParserTests.cs`:

```csharp
using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Tests;

public class ArgumentParserTests
{
    [Fact]
    public void ParseLine_splits_verb_args_and_flags()
    {
        var parsed = ArgumentParser.ParseLine("describe orders --output tsv --no-color");

        Assert.Equal("describe", parsed.Verb);
        Assert.Equal(new[] { "orders" }, parsed.Args);
        Assert.Equal("tsv", parsed.Flags["output"]);
        Assert.True(parsed.Flags.ContainsKey("no-color"));
        Assert.Null(parsed.Flags["no-color"]);
    }

    [Fact]
    public void ParseLine_keeps_quoted_values_together()
    {
        var parsed = ArgumentParser.ParseLine("produce orders --value \"hello world\"");

        Assert.Equal("hello world", parsed.Flags["value"]);
    }

    [Fact]
    public void ParseLine_returns_empty_verb_for_blank_input()
    {
        Assert.Equal("", ArgumentParser.ParseLine("   ").Verb);
    }

    [Fact]
    public void ParseArgv_behaves_like_ParseLine()
    {
        var parsed = ArgumentParser.ParseArgv(new[] { "topics", "--profile", "prod" });

        Assert.Equal("topics", parsed.Verb);
        Assert.Equal("prod", parsed.Flags["profile"]);
    }
}
```

- [x] **Step 3: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests`
Expected: FAIL com erro de compilação — `ArgumentParser` não existe.

- [x] **Step 4: Implementar os tipos base**

`Skat.KawkaProject.Tui/ExitCodes.cs`:

```csharp
namespace Skat.KawkaProject.Tui;

public static class ExitCodes
{
    public const int Success = 0;
    public const int OperationalFailure = 1;
    public const int Usage = 2;
    public const int ConfirmationRefused = 3;
}
```

`Skat.KawkaProject.Tui/Commands/CommandResult.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Commands;

public abstract record CommandResult
{
    public sealed record Table(
        string? Title,
        IReadOnlyList<string> Columns,
        IReadOnlyList<IReadOnlyList<string>> Rows) : CommandResult;

    public sealed record Pairs(string? Title, IReadOnlyDictionary<string, string> Values) : CommandResult;

    public sealed record Text(string Message) : CommandResult;

    public sealed record Failure(string Message, int ExitCode) : CommandResult;

    public int ExitCodeOrSuccess => this is Failure f ? f.ExitCode : ExitCodes.Success;
}
```

`Skat.KawkaProject.Tui/Commands/ParsedCommand.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Commands;

public sealed record ParsedCommand(
    string Verb,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string?> Flags)
{
    public string? Flag(string name) => Flags.TryGetValue(name, out var v) ? v : null;
    public bool HasFlag(string name) => Flags.ContainsKey(name);

    /// <summary>Reads a flag as int. Returns null when absent, throws FormatException when unparseable.</summary>
    public int? IntFlag(string name)
    {
        var raw = Flag(name);
        if (raw is null) return null;
        if (!int.TryParse(raw, out var value))
            throw new FormatException($"--{name} expects a number, got '{raw}'.");
        return value;
    }
}
```

`Skat.KawkaProject.Tui/Commands/ArgumentParser.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Commands;

public static class ArgumentParser
{
    public static ParsedCommand ParseLine(string line) => ParseArgv(Tokenize(line));

    public static ParsedCommand ParseArgv(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return new ParsedCommand("", Array.Empty<string>(), new Dictionary<string, string?>());

        var verb = tokens[0];
        var args = new List<string>();
        var flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { args.Add(token); continue; }

            var name = token[2..];
            // A flag takes the next token as its value unless that token is itself a flag.
            var hasValue = i + 1 < tokens.Count && !tokens[i + 1].StartsWith("--", StringComparison.Ordinal);
            flags[name] = hasValue ? tokens[++i] : null;
        }

        return new ParsedCommand(verb, args, flags);
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
```

- [x] **Step 5: Rodar os testes para confirmar que passam**

Run: `dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS, 4 testes.

- [x] **Step 6: Commit**

```bash
git add Skat.KawkaProject.Tui Skat.KawkaProject.Tui.Tests Skat.KawkaProject.sln
git commit -m "feat(tui): scaffold TUI project with command result types and argument parser"
```

---

### Task 2: Renderers

**Files:**
- Create: `Skat.KawkaProject.Tui/Rendering/IResultRenderer.cs`
- Create: `Skat.KawkaProject.Tui/Rendering/SpectreRenderer.cs`
- Create: `Skat.KawkaProject.Tui/Rendering/PlainTextRenderer.cs`
- Test: `Skat.KawkaProject.Tui.Tests/RendererTests.cs`

**Interfaces:**
- Consumes: `CommandResult` (Task 1).
- Produces: `IResultRenderer.Render(CommandResult)`. `SpectreRenderer(IAnsiConsole)` e `PlainTextRenderer(TextWriter output, TextWriter error)` — ambos injetáveis para teste.

- [x] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/RendererTests.cs`:

```csharp
using Spectre.Console;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Rendering;

namespace Skat.KawkaProject.Tui.Tests;

public class RendererTests
{
    private static CommandResult.Table SampleTable() => new(
        "Topics",
        new[] { "NAME", "PARTS" },
        new IReadOnlyList<string>[] { new[] { "orders", "4" }, new[] { "payments", "8" } });

    [Fact]
    public void PlainTextRenderer_emits_tab_separated_rows_without_ansi()
    {
        var output = new StringWriter();
        var renderer = new PlainTextRenderer(output, new StringWriter());

        renderer.Render(SampleTable());

        var text = output.ToString();
        Assert.Contains("NAME\tPARTS", text);
        Assert.Contains("orders\t4", text);
        Assert.DoesNotContain("[", text);   // no ANSI escapes
        Assert.DoesNotContain("│", text);          // no box drawing
    }

    [Fact]
    public void PlainTextRenderer_writes_failures_to_stderr()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var renderer = new PlainTextRenderer(output, error);

        renderer.Render(new CommandResult.Failure("nope", ExitCodes.Usage));

        Assert.Contains("nope", error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void SpectreRenderer_renders_all_rows()
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(new StringWriter())
        });
        var renderer = new SpectreRenderer(console);

        var record = new StringWriter();
        console.Profile.Out = new AnsiConsoleOutput(record);
        renderer.Render(SampleTable());

        var text = record.ToString();
        Assert.Contains("orders", text);
        Assert.Contains("payments", text);
    }
}
```

- [x] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~RendererTests"`
Expected: FAIL com erro de compilação.

- [x] **Step 3: Implementar os renderers**

`Skat.KawkaProject.Tui/Rendering/IResultRenderer.cs`:

```csharp
using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Rendering;

public interface IResultRenderer
{
    void Render(CommandResult result);
}
```

`Skat.KawkaProject.Tui/Rendering/PlainTextRenderer.cs`:

```csharp
using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Rendering;

/// <summary>
/// Renderer for non-TTY output (pipes, cron, CI): tab-separated, no ANSI, no box drawing,
/// so `cut` and `awk` can consume it. Failures go to stderr so stdout stays parseable.
/// </summary>
public sealed class PlainTextRenderer(TextWriter output, TextWriter error) : IResultRenderer
{
    public void Render(CommandResult result)
    {
        switch (result)
        {
            case CommandResult.Table t:
                output.WriteLine(string.Join('\t', t.Columns));
                foreach (var row in t.Rows) output.WriteLine(string.Join('\t', row));
                break;

            case CommandResult.Pairs p:
                foreach (var (k, v) in p.Values) output.WriteLine($"{k}\t{v}");
                break;

            case CommandResult.Text x:
                output.WriteLine(x.Message);
                break;

            case CommandResult.Failure f:
                error.WriteLine(f.Message);
                break;
        }
    }
}
```

`Skat.KawkaProject.Tui/Rendering/SpectreRenderer.cs`:

```csharp
using Spectre.Console;
using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Rendering;

/// <summary>Renderer for interactive terminals: bordered tables, colour, aligned columns.</summary>
public sealed class SpectreRenderer(IAnsiConsole console) : IResultRenderer
{
    public void Render(CommandResult result)
    {
        switch (result)
        {
            case CommandResult.Table t:
            {
                var table = new Table().Border(TableBorder.Rounded);
                if (t.Title is not null) table.Title = new TableTitle(t.Title);
                foreach (var c in t.Columns) table.AddColumn(new TableColumn($"[bold]{Markup.Escape(c)}[/]"));
                foreach (var row in t.Rows) table.AddRow(row.Select(cell => Markup.Escape(cell)).ToArray());
                console.Write(table);
                break;
            }

            case CommandResult.Pairs p:
            {
                var grid = new Grid().AddColumn().AddColumn();
                foreach (var (k, v) in p.Values)
                    grid.AddRow($"[dim]{Markup.Escape(k)}[/]", Markup.Escape(v));
                if (p.Title is not null) console.MarkupLine($"[bold]{Markup.Escape(p.Title)}[/]");
                console.Write(grid);
                break;
            }

            case CommandResult.Text x:
                console.MarkupLine(Markup.Escape(x.Message));
                break;

            case CommandResult.Failure f:
                console.MarkupLine($"[red]{Markup.Escape(f.Message)}[/]");
                break;
        }
    }
}
```

- [x] **Step 4: Rodar os testes**

Run: `dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui/Rendering Skat.KawkaProject.Tui.Tests/RendererTests.cs
git commit -m "feat(tui): add Spectre and plain-text result renderers"
```

---

### Task 3: Contrato de comando, registry e dispatcher

> **Achado do gate da Task 1, a honrar aqui:** o `ArgumentParser` normaliza o nome das flags
> (`StringComparer.OrdinalIgnoreCase`) mas **não** o verbo — `ParseLine("TOPICS")` devolve
> `Verb == "TOPICS"` cru. O lookup verbo→handler do `CommandRegistry` PRECISA ser
> `OrdinalIgnoreCase`, senão `TOPICS` vira "comando desconhecido" enquanto `--OUTPUT` funciona,
> uma assimetria que ninguém consegue adivinhar. Confirme também que o dispatcher captura
> `Exception` e não apenas tipos específicos: `ParsedCommand.IntFlag` lança `FormatException` para
> valor não numérico, e hoje não há ninguém entre ele e o topo do REPL.


O dispatcher é a **única** fronteira que captura `Exception`. É isso que impede cada comando de inventar seu próprio formato de mensagem de erro.

**Files:**
- Create: `Skat.KawkaProject.Tui/Commands/ITuiCommand.cs`, `CommandContext.cs`, `CommandRegistry.cs`, `CommandDispatcher.cs`
- Test: `Skat.KawkaProject.Tui.Tests/DispatcherTests.cs`

**Interfaces:**
- Consumes: `ParsedCommand`, `CommandResult`, `ExitCodes` (Task 1).
- Produces: `ITuiCommand`, `CommandContext`, `CommandRegistry.Resolve(string)`, `CommandRegistry.All`, `CommandDispatcher.DispatchAsync(ParsedCommand, IKafkaSession?, IConfirmer, CancellationToken)`. Todas as tasks de comando implementam `ITuiCommand`.

- [x] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/DispatcherTests.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class DispatcherTests
{
    private sealed class StubCommand(string name, bool requiresSession, Func<CommandContext, Task<CommandResult>> body) : ITuiCommand
    {
        public string Name => name;
        public string Usage => $"{name}";
        public string Summary => "stub";
        public bool RequiresSession => requiresSession;
        public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct) => body(ctx);
    }

    private static CommandDispatcher DispatcherWith(params ITuiCommand[] commands) =>
        new(new CommandRegistry(commands));

    private static readonly IConfirmer AlwaysYes = new StubConfirmer(true);

    private sealed class StubConfirmer(bool answer) : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct) => Task.FromResult(answer);
    }

    [Fact]
    public async Task Unknown_verb_is_a_usage_failure()
    {
        var result = await DispatcherWith().DispatchAsync(
            ArgumentParser.ParseLine("nope"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
        Assert.Contains("nope", failure.Message);
    }

    [Fact]
    public async Task Command_needing_a_session_fails_cleanly_when_there_is_none()
    {
        var cmd = new StubCommand("topics", true, _ => Task.FromResult<CommandResult>(new CommandResult.Text("ok")));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("topics"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
        Assert.Contains("connect", failure.Message);
    }

    [Fact]
    public async Task Exceptions_from_commands_become_operational_failures()
    {
        var cmd = new StubCommand("boom", false, _ => throw new InvalidOperationException("broker unreachable"));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("boom"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.OperationalFailure, failure.ExitCode);
        Assert.Contains("broker unreachable", failure.Message);
    }

    [Fact]
    public async Task Bad_flag_format_is_a_usage_failure_not_an_operational_one()
    {
        var cmd = new StubCommand("n", false, ctx => Task.FromResult<CommandResult>(
            new CommandResult.Text(ctx.Parsed.IntFlag("to")!.Value.ToString())));

        var result = await DispatcherWith(cmd).DispatchAsync(
            ArgumentParser.ParseLine("n --to abc"), null, AlwaysYes, CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
    }

    [Fact]
    public async Task Empty_input_is_a_no_op()
    {
        var result = await DispatcherWith().DispatchAsync(
            ArgumentParser.ParseLine("  "), null, AlwaysYes, CancellationToken.None);

        Assert.IsType<CommandResult.Text>(result);
    }
}
```

- [x] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~DispatcherTests"`
Expected: FAIL com erro de compilação.

- [x] **Step 3: Implementar contrato, registry e dispatcher**

`Skat.KawkaProject.Tui/Commands/ITuiCommand.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Commands;

public interface ITuiCommand
{
    string Name { get; }
    string Usage { get; }
    string Summary { get; }

    /// <summary>When true the dispatcher short-circuits with a usage failure if no session is open,
    /// so no handler needs to null-check the session.</summary>
    bool RequiresSession { get; }

    Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct);
}
```

`Skat.KawkaProject.Tui/Commands/CommandContext.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class CommandContext
{
    public required ParsedCommand Parsed { get; init; }
    public required IConfirmer Confirmer { get; init; }

    /// <summary>Null when no connection is open. Commands with RequiresSession never see null.</summary>
    public IKafkaSession? Session { get; init; }

    public IKafkaSession RequireSession() => Session
        ?? throw new InvalidOperationException("Session missing; RequiresSession should have short-circuited.");
}
```

`Skat.KawkaProject.Tui/Commands/CommandRegistry.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Commands;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, ITuiCommand> _byName;

    public CommandRegistry(IEnumerable<ITuiCommand> commands)
    {
        _byName = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ITuiCommand> All => _byName.Values;

    public ITuiCommand? Resolve(string verb) =>
        _byName.TryGetValue(verb, out var cmd) ? cmd : null;
}
```

`Skat.KawkaProject.Tui/Commands/CommandDispatcher.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Commands;

/// <summary>
/// The ONLY place that catches Exception. Individual commands let exceptions propagate, which is
/// what keeps error messages consistent instead of every handler inventing its own format.
/// </summary>
public sealed class CommandDispatcher(CommandRegistry registry)
{
    public async Task<CommandResult> DispatchAsync(
        ParsedCommand parsed, IKafkaSession? session, IConfirmer confirmer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parsed.Verb)) return new CommandResult.Text("");

        var command = registry.Resolve(parsed.Verb);
        if (command is null)
            return new CommandResult.Failure(
                $"Unknown command '{parsed.Verb}'. Type 'help' to see what is available.", ExitCodes.Usage);

        if (command.RequiresSession && session is null)
            return new CommandResult.Failure(
                "No active connection. Use 'connect <profile>' first.", ExitCodes.Usage);

        try
        {
            return await command.ExecuteAsync(
                new CommandContext { Parsed = parsed, Session = session, Confirmer = confirmer }, ct);
        }
        catch (OperationCanceledException)
        {
            return new CommandResult.Failure("Cancelled.", ExitCodes.OperationalFailure);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or ArgumentException)
        {
            // Argument problems are the user's typo, not the cluster's fault: usage, with the command's own usage line.
            return new CommandResult.Failure($"{ex.Message}\nUsage: {command.Usage}", ExitCodes.Usage);
        }
        catch (Exception ex)
        {
            return new CommandResult.Failure(ex.Message, ExitCodes.OperationalFailure);
        }
    }
}
```

`Skat.KawkaProject.Tui/Safety/IConfirmer.cs` (stub mínimo agora; implementações reais na Fase 4). **`DestructiveAction` NÃO é declarado aqui** — desde 2026-07-25 ele mora em `Skat.KawkaProject.Core.Models` como o lar único do "o que se perde", já consumido pelo GUI:

```csharp
using Skat.KawkaProject.Core.Models;   // DestructiveAction

namespace Skat.KawkaProject.Tui.Safety;

public interface IConfirmer
{
    Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct);
}
```

- [x] **Step 4: Rodar os testes**

Run: `dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS, 5 testes novos.

- [x] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui/Commands Skat.KawkaProject.Tui/Safety Skat.KawkaProject.Tui.Tests/DispatcherTests.cs
git commit -m "feat(tui): add command contract, registry and single-boundary dispatcher"
```

---

### Task 4: Comandos de conexão

**Files:**
- Create: `Skat.KawkaProject.Tui/Commands/ConnectionCommands.cs`
- Test: `Skat.KawkaProject.Tui.Tests/ConnectionCommandsTests.cs`

**Interfaces:**
- Consumes: `ITuiCommand`, `CommandContext`, `CommandResult` (Task 3); `IConnectionProfileRepository.GetAll()`, `IKafkaConnectionFactory.ConnectAsync(ConnectionProfile)`.
- Produces: `ProfilesCommand`, `ConnectCommand`, `DisconnectCommand`, `StatusCommand`. `ConnectCommand` expõe `IKafkaSession? Established` para o `TuiHost` capturar a sessão criada.

- [ ] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/ConnectionCommandsTests.cs`:

```csharp
using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class ConnectionCommandsTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private static CommandContext Ctx(string line, IKafkaSession? session = null) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = session,
        Confirmer = new NoConfirmer()
    };

    [Fact]
    public async Task Profiles_lists_saved_profiles_as_a_table()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(new[]
        {
            new ConnectionProfile { Name = "prod", BootstrapServers = "k1:9092", AuthType = AuthType.SaslSsl }
        });

        var result = await new ProfilesCommand(repo.Object).ExecuteAsync(Ctx("profiles"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Single(table.Rows);
        Assert.Equal("prod", table.Rows[0][0]);
        Assert.Contains("k1:9092", table.Rows[0][1]);
    }

    [Fact]
    public async Task Connect_opens_a_session_for_the_named_profile()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        var profile = new ConnectionProfile { Name = "prod", BootstrapServers = "k1:9092" };
        repo.Setup(r => r.GetAll()).Returns(new[] { profile });

        var session = new Mock<IKafkaSession>();
        session.Setup(s => s.ProfileName).Returns("prod");
        var factory = new Mock<IKafkaConnectionFactory>();
        factory.Setup(f => f.ConnectAsync(profile)).ReturnsAsync(session.Object);

        var cmd = new ConnectCommand(repo.Object, factory.Object);
        var result = await cmd.ExecuteAsync(Ctx("connect prod"), CancellationToken.None);

        Assert.IsType<CommandResult.Text>(result);
        Assert.Same(session.Object, cmd.Established);
    }

    [Fact]
    public async Task Connect_without_a_profile_name_is_a_usage_error()
    {
        var cmd = new ConnectCommand(Mock.Of<IConnectionProfileRepository>(), Mock.Of<IKafkaConnectionFactory>());

        var result = await cmd.ExecuteAsync(Ctx("connect"), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
    }

    [Fact]
    public async Task Connect_to_an_unknown_profile_names_the_available_ones()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(new[] { new ConnectionProfile { Name = "prod" } });

        var cmd = new ConnectCommand(repo.Object, Mock.Of<IKafkaConnectionFactory>());
        var result = await cmd.ExecuteAsync(Ctx("connect staging"), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Contains("prod", failure.Message);
    }

    [Fact]
    public async Task Status_reports_no_connection_when_there_is_none()
    {
        var result = await new StatusCommand().ExecuteAsync(Ctx("status"), CancellationToken.None);

        var text = Assert.IsType<CommandResult.Text>(result);
        Assert.Contains("No active connection", text.Message);
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~ConnectionCommandsTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar os comandos**

Criar `Skat.KawkaProject.Tui/Commands/ConnectionCommands.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class ProfilesCommand(IConnectionProfileRepository repo) : ITuiCommand
{
    public string Name => "profiles";
    public string Usage => "profiles";
    public string Summary => "List saved connection profiles";
    public bool RequiresSession => false;

    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var rows = repo.GetAll()
            .Select(p => (IReadOnlyList<string>)new[] { p.Name, p.BootstrapServers, p.AuthType.ToString() })
            .ToList();

        return Task.FromResult<CommandResult>(
            new CommandResult.Table("Profiles", new[] { "NAME", "BOOTSTRAP", "AUTH" }, rows));
    }
}

public sealed class ConnectCommand(IConnectionProfileRepository repo, IKafkaConnectionFactory factory) : ITuiCommand
{
    public string Name => "connect";
    public string Usage => "connect <profile>";
    public string Summary => "Open a session against a saved profile";
    public bool RequiresSession => false;

    /// <summary>Set on success so the host can take ownership of the new session.</summary>
    public IKafkaSession? Established { get; private set; }

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        Established = null;

        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing profile name. Usage: {Usage}", ExitCodes.Usage);

        var name = ctx.Parsed.Args[0];
        var all = repo.GetAll();
        var profile = all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            var known = all.Count == 0 ? "(none saved)" : string.Join(", ", all.Select(p => p.Name));
            return new CommandResult.Failure($"No profile named '{name}'. Available: {known}", ExitCodes.Usage);
        }

        Established = await factory.ConnectAsync(profile);
        return new CommandResult.Text($"Connected to '{profile.Name}' ({profile.BootstrapServers}).");
    }
}

public sealed class DisconnectCommand : ITuiCommand
{
    public string Name => "disconnect";
    public string Usage => "disconnect";
    public string Summary => "Close the active session";
    public bool RequiresSession => true;

    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct) =>
        Task.FromResult<CommandResult>(new CommandResult.Text($"Disconnected from '{ctx.RequireSession().ProfileName}'."));
}

public sealed class StatusCommand : ITuiCommand
{
    public string Name => "status";
    public string Usage => "status";
    public string Summary => "Show the active connection";
    public bool RequiresSession => false;

    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct) =>
        Task.FromResult<CommandResult>(ctx.Session is null
            ? new CommandResult.Text("No active connection.")
            : new CommandResult.Text($"Connected to '{ctx.Session.ProfileName}' ({ctx.Session.BootstrapServers})."));
}
```

Nota de implementação: `DisconnectCommand` só reporta; quem descarta a sessão é o `TuiHost` (Task 6), porque o ciclo de vida da sessão pertence ao host, não ao comando.

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui/Commands/ConnectionCommands.cs Skat.KawkaProject.Tui.Tests/ConnectionCommandsTests.cs
git commit -m "feat(tui): add profiles, connect, disconnect and status commands"
```

---

### Task 5: Comandos de leitura de tópicos

> **Achado do gate da Task 2, a honrar aqui:** o `DescribeCommand` desenhado abaixo carrega os
> **config overrides do tópico dentro do `Title`** da `Table`, com o comentário de que "os overrides
> viajam no título para um único resultado carregar os dois fatos". Isso perde a informação em modo
> pipe: o `PlainTextRenderer` descarta títulos de propósito (são decoração, e um pipeline
> `describe orders | cut -f1` não deve ter de saber pular uma linha). Resultado: `describe` por pipe
> mostraria as partições e **omitiria os overrides em silêncio**.
>
> Corrija na implementação: o título fica para decoração, e os config overrides saem como dado de
> verdade — uma `Pairs` adicional, ou linhas próprias na tabela. A regra geral, já documentada no
> `PlainTextRenderer`: **se um fato importa, ele vai numa coluna ou num par, nunca num título.**


**Files:**
- Create: `Skat.KawkaProject.Tui/Commands/TopicCommands.cs`
- Test: `Skat.KawkaProject.Tui.Tests/TopicCommandsTests.cs`

**Interfaces:**
- Consumes: `ITopicService.ListTopicsAsync`, `GetTopicDetailAsync`, `GetTopicConfigOverridesAsync`.
- Produces: `TopicsCommand`, `DescribeCommand`.

> **Nota:** `GetTopicConfigOverridesAsync` é o nome pós-Task 2 do plano de hardening. Se a Fase 1 da TUI for feita antes daquele rename, use `GetTopicConfigAsync` e ajuste ao renomear.

- [ ] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/TopicCommandsTests.cs`:

```csharp
using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class TopicCommandsTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private static IKafkaSession FakeSession()
    {
        var m = new Mock<IKafkaSession>();
        m.Setup(s => s.ProfileName).Returns("test");
        return m.Object;
    }

    private static CommandContext Ctx(string line) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = FakeSession(),
        Confirmer = new NoConfirmer()
    };

    [Fact]
    public async Task Topics_lists_every_topic()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 3), new TopicInfo("payments", 8, 3) });

        var result = await new TopicsCommand(svc.Object).ExecuteAsync(Ctx("topics"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public async Task Topics_filters_by_substring_case_insensitively()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 3), new TopicInfo("payments", 8, 3) });

        var result = await new TopicsCommand(svc.Object).ExecuteAsync(Ctx("topics ORDER"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Single(table.Rows);
        Assert.Equal("orders", table.Rows[0][0]);
    }

    [Fact]
    public async Task Describe_returns_partitions_and_config_overrides()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 2, 3),
               new List<PartitionInfo> { new(0, 1, 0, 1204), new(1, 2, 0, 987) }));
        svc.Setup(s => s.GetTopicConfigOverridesAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new Dictionary<string, string> { ["retention.ms"] = "604800000" });

        var result = await new DescribeCommand(svc.Object).ExecuteAsync(Ctx("describe orders"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal(2, table.Rows.Count);
        Assert.Contains("retention.ms=604800000", table.Title);
    }

    [Fact]
    public async Task Describe_without_a_topic_name_is_a_usage_error()
    {
        var result = await new DescribeCommand(Mock.Of<ITopicService>())
            .ExecuteAsync(Ctx("describe"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~TopicCommandsTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar os comandos**

Criar `Skat.KawkaProject.Tui/Commands/TopicCommands.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class TopicsCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "topics";
    public string Usage => "topics [filter]";
    public string Summary => "List topics, optionally filtered by substring";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var all = await topics.ListTopicsAsync(ctx.RequireSession());
        var filter = ctx.Parsed.Args.Count > 0 ? ctx.Parsed.Args[0] : null;

        var rows = all
            .Where(t => filter is null || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (IReadOnlyList<string>)new[]
            {
                t.Name, t.PartitionCount.ToString(), t.ReplicationFactor.ToString()
            })
            .ToList();

        return new CommandResult.Table(
            filter is null ? "Topics" : $"Topics matching '{filter}'",
            new[] { "NAME", "PARTS", "RF" }, rows);
    }
}

public sealed class DescribeCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "describe";
    public string Usage => "describe <topic>";
    public string Summary => "Show partitions, offsets and config overrides for a topic";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var session = ctx.RequireSession();
        var topicName = ctx.Parsed.Args[0];
        var detail = await topics.GetTopicDetailAsync(session, topicName);
        var overrides = await topics.GetTopicConfigOverridesAsync(session, topicName);

        // The config overrides ride in the title so a single Table result carries both facts;
        // an empty override set says so explicitly rather than showing nothing (see spec: an empty
        // result means "no overrides", not "no configuration").
        var configNote = overrides.Count == 0
            ? "no config overrides"
            : string.Join("  ", overrides.Select(kv => $"{kv.Key}={kv.Value}"));

        var title = $"{detail.Topic.Name} · {detail.Partitions.Count} partitions · RF {detail.Topic.ReplicationFactor} · {configNote}";

        var rows = detail.Partitions
            .OrderBy(p => p.PartitionId)
            .Select(p => (IReadOnlyList<string>)new[]
            {
                p.PartitionId.ToString(), p.LeaderBrokerId.ToString(),
                p.EarliestOffset.ToString("N0"), p.LatestOffset.ToString("N0"),
                (p.LatestOffset - p.EarliestOffset).ToString("N0")
            })
            .ToList();

        return new CommandResult.Table(title,
            new[] { "P", "LEADER", "EARLIEST", "LATEST", "COUNT" }, rows);
    }
}
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui/Commands/TopicCommands.cs Skat.KawkaProject.Tui.Tests/TopicCommandsTests.cs
git commit -m "feat(tui): add topics and describe commands"
```

---

### Task 6: `Program`, `TuiHost` e o comando `help`

> **Achado do gate da Task 3, a honrar aqui:** `CommandRegistry.All` devolve `_byName.Values`, ou
> seja, a ordem de inserção do dicionário — que é a ordem de registro no composition root, arbitrária
> do ponto de vista de quem lê a ajuda (e nem sequer é contrato documentado do `Dictionary`). O
> `HelpCommand` deve **ordenar explicitamente por `Name`** ao renderizar, em vez de confiar em `All`.


Fecha a Fase 1: a ferramenta passa a rodar de verdade, nos dois modos. O REPL usa `Console.ReadLine()` por enquanto — a caixa com borda vem na Fase 2.

**Files:**
- Create: `Skat.KawkaProject.Tui/Program.cs`, `TuiHost.cs`, `Commands/HelpCommand.cs`
- Test: `Skat.KawkaProject.Tui.Tests/HelpCommandTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 1-5.
- Produces: `TuiHost.RunReplAsync(CancellationToken)`, `TuiHost.RunOnceAsync(ParsedCommand, CancellationToken)`, `HelpCommand`.

- [ ] **Step 1: Escrever o teste do help**

Criar `Skat.KawkaProject.Tui.Tests/HelpCommandTests.cs`:

```csharp
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class HelpCommandTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class Stub(string name) : ITuiCommand
    {
        public string Name => name;
        public string Usage => $"{name} <arg>";
        public string Summary => $"does {name}";
        public bool RequiresSession => false;
        public Task<CommandResult> ExecuteAsync(CommandContext c, CancellationToken ct) =>
            Task.FromResult<CommandResult>(new CommandResult.Text("ok"));
    }

    private static CommandContext Ctx(string line) => new()
    {
        Parsed = ArgumentParser.ParseLine(line), Confirmer = new NoConfirmer()
    };

    [Fact]
    public async Task Help_lists_every_registered_command()
    {
        var registry = new CommandRegistry(new ITuiCommand[] { new Stub("topics"), new Stub("describe") });

        var result = await new HelpCommand(registry).ExecuteAsync(Ctx("help"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public async Task Help_for_one_command_shows_its_usage()
    {
        var registry = new CommandRegistry(new ITuiCommand[] { new Stub("topics") });

        var result = await new HelpCommand(registry).ExecuteAsync(Ctx("help topics"), CancellationToken.None);

        Assert.Contains("topics <arg>", Assert.IsType<CommandResult.Text>(result).Message);
    }

    [Fact]
    public async Task Help_for_an_unknown_command_is_a_usage_failure()
    {
        var registry = new CommandRegistry(Array.Empty<ITuiCommand>());

        var result = await new HelpCommand(registry).ExecuteAsync(Ctx("help nope"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~HelpCommandTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar `HelpCommand`**

Criar `Skat.KawkaProject.Tui/Commands/HelpCommand.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Commands;

public sealed class HelpCommand(CommandRegistry registry) : ITuiCommand
{
    public string Name => "help";
    public string Usage => "help [command]";
    public string Summary => "Show available commands, or details of one";
    public bool RequiresSession => false;

    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count > 0)
        {
            var name = ctx.Parsed.Args[0];
            var cmd = registry.Resolve(name);
            return Task.FromResult<CommandResult>(cmd is null
                ? new CommandResult.Failure($"Unknown command '{name}'.", ExitCodes.Usage)
                : new CommandResult.Text($"{cmd.Usage}\n  {cmd.Summary}"));
        }

        var rows = registry.All
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => (IReadOnlyList<string>)new[] { c.Usage, c.Summary })
            .ToList();

        return Task.FromResult<CommandResult>(
            new CommandResult.Table("Commands", new[] { "USAGE", "WHAT IT DOES" }, rows));
    }
}
```

- [ ] **Step 4: Implementar `TuiHost`**

Criar `Skat.KawkaProject.Tui/TuiHost.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui;

/// <summary>
/// Owns the session lifecycle and the REPL loop. Commands never dispose the session:
/// it belongs to the host, which is the only thing that outlives a single command.
/// </summary>
public sealed class TuiHost(
    CommandDispatcher dispatcher,
    CommandRegistry registry,
    IResultRenderer renderer,
    IConfirmer confirmer) : IDisposable
{
    private IKafkaSession? _session;
    private CancellationTokenSource? _runningCommand;

    public async Task<int> RunOnceAsync(ParsedCommand parsed, CancellationToken ct)
    {
        var result = await dispatcher.DispatchAsync(parsed, _session, confirmer, ct);
        AdoptSessionIfConnected(parsed);
        renderer.Render(result);
        return result.ExitCodeOrSuccess;
    }

    /// <summary>
    /// Cancels the command currently executing, if any. Returns false when the prompt is idle,
    /// which is the caller's signal that Ctrl+C should exit instead of cancelling.
    /// </summary>
    public bool CancelRunningCommand()
    {
        var running = _runningCommand;
        if (running is null || running.IsCancellationRequested) return false;
        running.Cancel();
        return true;
    }

    public async Task<int> RunReplAsync(CancellationToken ct)
    {
        renderer.Render(new CommandResult.Text("kawka · type 'help' for commands, 'exit' to quit"));

        while (!ct.IsCancellationRequested)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null) break;                                  // EOF (Ctrl+D)
            if (line.Trim() is "exit" or "quit") break;

            var parsed = ArgumentParser.ParseLine(line);

            // A fresh token per command, linked to the global one: Ctrl+C during a command
            // cancels only that command and the loop keeps going. A failed or cancelled
            // command never kills the REPL.
            using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runningCommand = commandCts;
            try
            {
                var result = await dispatcher.DispatchAsync(parsed, _session, confirmer, commandCts.Token);
                AdoptSessionIfConnected(parsed);
                HandleDisconnect(parsed, result);
                renderer.Render(result);
            }
            finally
            {
                _runningCommand = null;
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>Takes ownership of a session created by ConnectCommand, disposing any previous one.</summary>
    private void AdoptSessionIfConnected(ParsedCommand parsed)
    {
        if (registry.Resolve(parsed.Verb) is not ConnectCommand connect) return;
        if (connect.Established is null) return;

        _session?.Dispose();
        _session = connect.Established;
    }

    private void HandleDisconnect(ParsedCommand parsed, CommandResult result)
    {
        if (parsed.Verb.Equals("disconnect", StringComparison.OrdinalIgnoreCase)
            && result is not CommandResult.Failure)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    public void Dispose() => _session?.Dispose();
}
```

- [ ] **Step 5: Implementar `Program`**

Criar `Skat.KawkaProject.Tui/Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Kafka;
using Skat.KawkaProject.Tui;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Rendering;
using Skat.KawkaProject.Tui.Safety;

var parsed = ArgumentParser.ParseArgv(args);
var oneShot = !string.IsNullOrWhiteSpace(parsed.Verb);

// TTY detection happens ONCE, here, so no code downstream needs to branch on it.
var wantsPlain = parsed.Flag("output") == "tsv"
                 || (!AnsiConsole.Profile.Capabilities.Interactive && parsed.Flag("output") != "text");

var services = new ServiceCollection();
services.AddSingleton<IConnectionProfileRepository, ConnectionProfileRepository>();
services.AddSingleton<IKafkaConnectionFactory, KafkaConnectionFactory>();
services.AddTransient<ITopicService, TopicService>();
services.AddTransient<IMessageService, MessageService>();
services.AddTransient<IClusterService, ClusterService>();

services.AddSingleton<ITuiCommand, ProfilesCommand>();
services.AddSingleton<ITuiCommand, ConnectCommand>();
services.AddSingleton<ITuiCommand, DisconnectCommand>();
services.AddSingleton<ITuiCommand, StatusCommand>();
services.AddSingleton<ITuiCommand, TopicsCommand>();
services.AddSingleton<ITuiCommand, DescribeCommand>();

services.AddSingleton<CommandRegistry>(sp =>
{
    var commands = sp.GetServices<ITuiCommand>().ToList();
    var registry = new CommandRegistry(commands);
    return new CommandRegistry(commands.Append(new HelpCommand(registry)));
});
services.AddSingleton<CommandDispatcher>();

services.AddSingleton<IResultRenderer>(_ => wantsPlain
    ? new PlainTextRenderer(Console.Out, Console.Error)
    : new SpectreRenderer(AnsiConsole.Console));

// Phase 4 replaces this with the real confirmers.
services.AddSingleton<IConfirmer>(_ => new NotYetImplementedConfirmer());

services.AddSingleton<TuiHost>();

using var provider = services.BuildServiceProvider();
using var host = provider.GetRequiredService<TuiHost>();

using var cts = new CancellationTokenSource();

// Ctrl+C during a command cancels just that command and returns the prompt; Ctrl+C at an idle
// prompt exits. Matches the Claude Code behaviour the layout is modelled on.
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (!host.CancelRunningCommand()) cts.Cancel();
};

if (oneShot)
{
    // One-shot needs its session established up front, since there is no REPL to 'connect' in.
    var profile = parsed.Flag("profile");
    if (profile is not null)
        await host.RunOnceAsync(ArgumentParser.ParseLine($"connect {profile}"), cts.Token);

    return await host.RunOnceAsync(parsed, cts.Token);
}

return await host.RunReplAsync(cts.Token);

file sealed class NotYetImplementedConfirmer : IConfirmer
{
    public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct) =>
        throw new NotSupportedException("Destructive commands are not available yet (Phase 4).");
}
```

- [ ] **Step 6: Verificar build, testes e execução real**

```bash
dotnet build && dotnet test Skat.KawkaProject.Tui.Tests
dotnet run --project Skat.KawkaProject.Tui -- help
dotnet run --project Skat.KawkaProject.Tui -- help | cat        # sem TTY: deve sair TSV
```
Expected: build limpo; testes verdes; `help` lista os comandos; a versão com `| cat` sai sem bordas nem ANSI.

- [ ] **Step 7: Commit**

```bash
git add Skat.KawkaProject.Tui Skat.KawkaProject.Tui.Tests
git commit -m "feat(tui): add help command, REPL host and one-shot entry point"
```

---

# FASE 2 — Caixa de prompt

Isolada de propósito: é a parte mais trabalhosa e a de menor risco funcional. Até aqui o REPL usa `Console.ReadLine()`; esta fase entrega a caixa com borda, setas e histórico.

### Task 7: `IKeySource` e `LineHistory`

**Files:**
- Create: `Skat.KawkaProject.Tui/Input/IKeySource.cs`, `ConsoleKeySource.cs`, `LineHistory.cs`
- Test: `Skat.KawkaProject.Tui.Tests/LineHistoryTests.cs`

**Interfaces:**
- Produces: `IKeySource.ReadKey()`, `LineHistory.Add(string)`, `LineHistory.Previous()`, `LineHistory.Next()`, `LineHistory.ResetCursor()`, `LineHistory.Load(string path)`, `LineHistory.Save(string path)`. Task 8 consome ambos.

- [ ] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/LineHistoryTests.cs`:

```csharp
using Skat.KawkaProject.Tui.Input;

namespace Skat.KawkaProject.Tui.Tests;

public class LineHistoryTests
{
    [Fact]
    public void Previous_walks_backwards_then_stops_at_the_oldest()
    {
        var h = new LineHistory();
        h.Add("topics"); h.Add("describe orders");

        Assert.Equal("describe orders", h.Previous());
        Assert.Equal("topics", h.Previous());
        Assert.Equal("topics", h.Previous());
    }

    [Fact]
    public void Next_walks_forward_and_returns_empty_past_the_newest()
    {
        var h = new LineHistory();
        h.Add("a"); h.Add("b");
        h.Previous(); h.Previous();

        Assert.Equal("b", h.Next());
        Assert.Equal("", h.Next());
    }

    [Fact]
    public void Add_ignores_blanks_and_consecutive_duplicates()
    {
        var h = new LineHistory();
        h.Add("topics"); h.Add("topics"); h.Add("   ");

        Assert.Equal("topics", h.Previous());
        Assert.Equal("topics", h.Previous());   // only one entry exists
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kawka-hist-{Guid.NewGuid():N}");
        try
        {
            var written = new LineHistory();
            written.Add("topics"); written.Add("brokers");
            written.Save(path);

            var read = new LineHistory();
            read.Load(path);

            Assert.Equal("brokers", read.Previous());
            Assert.Equal("topics", read.Previous());
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~LineHistoryTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar**

`Skat.KawkaProject.Tui/Input/IKeySource.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Input;

/// <summary>
/// Abstracts keyboard reading so PromptReader can be tested with a scripted key sequence
/// instead of a real console.
/// </summary>
public interface IKeySource
{
    ConsoleKeyInfo ReadKey();
}

public sealed class ConsoleKeySource : IKeySource
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);
}
```

`Skat.KawkaProject.Tui/Input/LineHistory.cs`:

```csharp
namespace Skat.KawkaProject.Tui.Input;

public sealed class LineHistory
{
    private readonly List<string> _entries = new();
    private int _cursor;                     // _entries.Count means "past the newest" (empty line)

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) { ResetCursor(); return; }
        if (_entries.Count > 0 && _entries[^1] == line) { ResetCursor(); return; }
        _entries.Add(line);
        ResetCursor();
    }

    public void ResetCursor() => _cursor = _entries.Count;

    public string Previous()
    {
        if (_entries.Count == 0) return "";
        if (_cursor > 0) _cursor--;
        return _entries[_cursor];
    }

    public string Next()
    {
        if (_entries.Count == 0) return "";
        if (_cursor < _entries.Count) _cursor++;
        return _cursor >= _entries.Count ? "" : _entries[_cursor];
    }

    public void Load(string path)
    {
        if (!File.Exists(path)) return;
        _entries.Clear();
        _entries.AddRange(File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)));
        ResetCursor();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(path, _entries.TakeLast(500));
    }
}
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui/Input Skat.KawkaProject.Tui.Tests/LineHistoryTests.cs
git commit -m "feat(tui): add key source abstraction and command history"
```

---

### Task 8: `PromptReader` e integração no host

**Files:**
- Create: `Skat.KawkaProject.Tui/Input/PromptReader.cs`
- Modify: `Skat.KawkaProject.Tui/TuiHost.cs`, `Skat.KawkaProject.Tui/Program.cs`
- Test: `Skat.KawkaProject.Tui.Tests/PromptReaderTests.cs`

**Interfaces:**
- Consumes: `IKeySource`, `LineHistory` (Task 7).
- Produces: `PromptReader(IKeySource keys, LineHistory history, IAnsiConsole console)` com `string? ReadLine()` — devolve `null` em Ctrl+D numa linha vazia.

- [ ] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/PromptReaderTests.cs`:

```csharp
using Spectre.Console;
using Skat.KawkaProject.Tui.Input;

namespace Skat.KawkaProject.Tui.Tests;

public class PromptReaderTests
{
    private sealed class ScriptedKeys(params ConsoleKeyInfo[] keys) : IKeySource
    {
        private int _i;
        public ConsoleKeyInfo ReadKey() => _i < keys.Length ? keys[_i++]
            : new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
    }

    private static ConsoleKeyInfo Ch(char c) => new(c, ConsoleKey.A, false, false, false);
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);

    private static IAnsiConsole SilentConsole() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(new StringWriter())
    });

    private static PromptReader Reader(LineHistory history, params ConsoleKeyInfo[] keys) =>
        new(new ScriptedKeys(keys), history, SilentConsole());

    [Fact]
    public void Typed_characters_become_the_line()
    {
        var line = Reader(new LineHistory(), Ch('h'), Ch('i'), Key(ConsoleKey.Enter)).ReadLine();

        Assert.Equal("hi", line);
    }

    [Fact]
    public void Backspace_removes_the_last_character()
    {
        var line = Reader(new LineHistory(), Ch('a'), Ch('b'), Key(ConsoleKey.Backspace), Key(ConsoleKey.Enter)).ReadLine();

        Assert.Equal("a", line);
    }

    [Fact]
    public void Up_arrow_recalls_the_previous_command()
    {
        var history = new LineHistory();
        history.Add("topics");

        var line = Reader(history, Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)).ReadLine();

        Assert.Equal("topics", line);
    }

    [Fact]
    public void Submitted_lines_are_added_to_history()
    {
        var history = new LineHistory();

        Reader(history, Ch('x'), Key(ConsoleKey.Enter)).ReadLine();

        Assert.Equal("x", history.Previous());
    }

    [Fact]
    public void CtrlD_on_an_empty_line_returns_null()
    {
        var keys = new[] { new ConsoleKeyInfo('', ConsoleKey.D, false, false, control: true) };

        Assert.Null(Reader(new LineHistory(), keys).ReadLine());
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~PromptReaderTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar `PromptReader`**

Criar `Skat.KawkaProject.Tui/Input/PromptReader.cs`:

```csharp
using Spectre.Console;

namespace Skat.KawkaProject.Tui.Input;

/// <summary>
/// The bordered input box at the bottom of the screen, with history and in-line editing.
/// Spectre's TextPrompt is a single plain line, so this is hand-rolled: it owns the key loop,
/// the edit buffer and the redraw. Keyboard input comes through IKeySource so tests can script it.
/// </summary>
public sealed class PromptReader(IKeySource keys, LineHistory history, IAnsiConsole console)
{
    private const string Prompt = "> ";

    /// <summary>Reads one line. Returns null on Ctrl+D at an empty prompt (EOF).</summary>
    public string? ReadLine()
    {
        var buffer = new System.Text.StringBuilder();
        Redraw(buffer.ToString());

        while (true)
        {
            var key = keys.ReadKey();

            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (buffer.Length == 0) return null;
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                {
                    var line = buffer.ToString();
                    history.Add(line);
                    console.WriteLine();
                    return line;
                }

                case ConsoleKey.Backspace:
                    if (buffer.Length > 0) buffer.Length--;
                    break;

                case ConsoleKey.UpArrow:
                    buffer.Clear().Append(history.Previous());
                    break;

                case ConsoleKey.DownArrow:
                    buffer.Clear().Append(history.Next());
                    break;

                case ConsoleKey.Escape:
                    buffer.Clear();
                    history.ResetCursor();
                    break;

                default:
                    if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
                    break;
            }

            Redraw(buffer.ToString());
        }
    }

    private void Redraw(string current)
    {
        // Rewriting the whole box on every keystroke is fine at terminal speeds and keeps the
        // logic trivial; the alternative (differential cursor movement) buys nothing here.
        console.Write(new Panel(new Markup(Markup.Escape(Prompt + current)))
        {
            Border = BoxBorder.Rounded,
            Expand = true
        });
    }
}
```

- [ ] **Step 4: Ligar o `PromptReader` ao host**

Em `Skat.KawkaProject.Tui/TuiHost.cs`, adicionar `PromptReader? promptReader = null` como último parâmetro do construtor primário e substituir as duas linhas de leitura em `RunReplAsync`:

```csharp
            string? line;
            if (promptReader is not null) line = promptReader.ReadLine();
            else { Console.Write("> "); line = Console.ReadLine(); }
```

Em `Skat.KawkaProject.Tui/Program.cs`, registrar antes de `services.AddSingleton<TuiHost>()`:

```csharp
var historyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kawka", "history");

services.AddSingleton<IKeySource, ConsoleKeySource>();
services.AddSingleton<LineHistory>(_ => { var h = new LineHistory(); h.Load(historyPath); return h; });
services.AddSingleton<PromptReader>(sp => new PromptReader(
    sp.GetRequiredService<IKeySource>(), sp.GetRequiredService<LineHistory>(), AnsiConsole.Console));
```

E, no fim do arquivo, salvar o histórico antes de sair do REPL:

```csharp
var replExit = await host.RunReplAsync(cts.Token);
provider.GetRequiredService<LineHistory>().Save(historyPath);
return replExit;
```

A caixa só faz sentido com TTY — em entrada redirecionada ela desenharia molduras no meio de um pipe. Substituir o registro `services.AddSingleton<TuiHost>();` por uma factory que passa `null` nesse caso, fazendo o host cair no `Console.ReadLine()`:

```csharp
services.AddSingleton<TuiHost>(sp => new TuiHost(
    sp.GetRequiredService<CommandDispatcher>(),
    sp.GetRequiredService<CommandRegistry>(),
    sp.GetRequiredService<IResultRenderer>(),
    sp.GetRequiredService<IConfirmer>(),
    wantsPlain ? null : sp.GetRequiredService<PromptReader>()));
```

- [ ] **Step 5: Rodar testes e verificar manualmente**

```bash
dotnet build && dotnet test Skat.KawkaProject.Tui.Tests
dotnet run --project Skat.KawkaProject.Tui
```
Expected: testes verdes; o REPL abre com a caixa com borda; digitar `help` e Enter funciona; seta pra cima recupera o comando anterior; Ctrl+D sai.

- [ ] **Step 6: Commit**

```bash
git add Skat.KawkaProject.Tui Skat.KawkaProject.Tui.Tests/PromptReaderTests.cs
git commit -m "feat(tui): add bordered prompt box with history and inline editing"
```

---

# FASE 3 — Cluster e mensagens

### Task 9: Comandos de cluster

**Files:**
- Create: `Skat.KawkaProject.Tui/Commands/ClusterCommands.cs`
- Modify: `Skat.KawkaProject.Tui/Program.cs` (registrar os três)
- Test: `Skat.KawkaProject.Tui.Tests/ClusterCommandsTests.cs`

**Interfaces:**
- Consumes: `IClusterService.ListBrokersAsync`, `ListConsumerGroupsAsync`, `GetGroupLagAsync`.
- Produces: `BrokersCommand`, `GroupsCommand`, `LagCommand`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/ClusterCommandsTests.cs`:

```csharp
using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class ClusterCommandsTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private static CommandContext Ctx(string line) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = Mock.Of<IKafkaSession>(),
        Confirmer = new NoConfirmer()
    };

    [Fact]
    public async Task Brokers_marks_the_controller()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.ListBrokersAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new BrokerInfo(1, "k1", 9092, true), new BrokerInfo(2, "k2", 9092, false) });

        var result = await new BrokersCommand(svc.Object).ExecuteAsync(Ctx("brokers"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal("yes", table.Rows[0][3]);
        Assert.Equal("", table.Rows[1][3]);
    }

    [Fact]
    public async Task Lag_totals_the_lag_across_partitions()
    {
        var svc = new Mock<IClusterService>();
        svc.Setup(s => s.GetGroupLagAsync(It.IsAny<IKafkaSession>(), "billing"))
           .ReturnsAsync(new[]
           {
               new PartitionLag("orders", 0, 100, 150, 50),
               new PartitionLag("orders", 1, 200, 210, 10)
           });

        var result = await new LagCommand(svc.Object).ExecuteAsync(Ctx("lag billing"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Equal(2, table.Rows.Count);
        Assert.Contains("60", table.Title);
    }

    [Fact]
    public async Task Lag_without_a_group_is_a_usage_error()
    {
        var result = await new LagCommand(Mock.Of<IClusterService>())
            .ExecuteAsync(Ctx("lag"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~ClusterCommandsTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar**

Criar `Skat.KawkaProject.Tui/Commands/ClusterCommands.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class BrokersCommand(IClusterService cluster) : ITuiCommand
{
    public string Name => "brokers";
    public string Usage => "brokers";
    public string Summary => "List cluster brokers";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var rows = (await cluster.ListBrokersAsync(ctx.RequireSession()))
            .OrderBy(b => b.BrokerId)
            .Select(b => (IReadOnlyList<string>)new[]
            {
                b.BrokerId.ToString(), b.Host, b.Port.ToString(), b.IsController ? "yes" : ""
            })
            .ToList();

        return new CommandResult.Table("Brokers", new[] { "ID", "HOST", "PORT", "CONTROLLER" }, rows);
    }
}

public sealed class GroupsCommand(IClusterService cluster) : ITuiCommand
{
    public string Name => "groups";
    public string Usage => "groups";
    public string Summary => "List consumer groups";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        var rows = (await cluster.ListConsumerGroupsAsync(ctx.RequireSession()))
            .OrderBy(g => g.GroupId, StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<string>)new[] { g.GroupId, g.State, g.MemberCount.ToString() })
            .ToList();

        return new CommandResult.Table("Consumer groups", new[] { "GROUP", "STATE", "MEMBERS" }, rows);
    }
}

public sealed class LagCommand(IClusterService cluster) : ITuiCommand
{
    public string Name => "lag";
    public string Usage => "lag <group>";
    public string Summary => "Show per-partition lag for a consumer group";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing group id. Usage: {Usage}", ExitCodes.Usage);

        var group = ctx.Parsed.Args[0];
        var lags = (await cluster.GetGroupLagAsync(ctx.RequireSession(), group)).ToList();
        var total = lags.Sum(l => l.Lag);

        var rows = lags
            .OrderBy(l => l.Topic, StringComparer.Ordinal).ThenBy(l => l.Partition)
            .Select(l => (IReadOnlyList<string>)new[]
            {
                l.Topic, l.Partition.ToString(),
                l.CurrentOffset.ToString("N0"), l.EndOffset.ToString("N0"), l.Lag.ToString("N0")
            })
            .ToList();

        return new CommandResult.Table($"Lag for '{group}' · total {total:N0}",
            new[] { "TOPIC", "P", "CURRENT", "END", "LAG" }, rows);
    }
}
```

Registrar em `Program.cs`, junto dos outros comandos:

```csharp
services.AddSingleton<ITuiCommand, BrokersCommand>();
services.AddSingleton<ITuiCommand, GroupsCommand>();
services.AddSingleton<ITuiCommand, LagCommand>();
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet build && dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui Skat.KawkaProject.Tui.Tests/ClusterCommandsTests.cs
git commit -m "feat(tui): add brokers, groups and lag commands"
```

---

### Task 10: Comandos de mensagem

**Files:**
- Create: `Skat.KawkaProject.Tui/Commands/MessageCommands.cs`
- Modify: `Skat.KawkaProject.Tui/Program.cs`
- Test: `Skat.KawkaProject.Tui.Tests/MessageCommandsTests.cs`

**Interfaces:**
- Consumes: `IMessageService.FetchMessagesAsync(session, topic, int partition, long startOffset, int count)`, `ProduceAsync(session, topic, string? key, string value)`; `ITopicService.GetTopicDetailAsync` para resolver `--from`.
- Produces: `ConsumeCommand`, `ProduceCommand`.

> **`produce` não tem `--partition`:** `IMessageService.ProduceAsync` não aceita partição. Não invente a flag.

- [ ] **Step 1: Escrever o teste que falha**

Criar `Skat.KawkaProject.Tui.Tests/MessageCommandsTests.cs`:

```csharp
using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class MessageCommandsTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private static CommandContext Ctx(string line) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = Mock.Of<IKafkaSession>(),
        Confirmer = new NoConfirmer()
    };

    private static Mock<ITopicService> TopicWithOffsets(long earliest, long latest)
    {
        var topics = new Mock<ITopicService>();
        topics.Setup(t => t.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
              .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 1, 1),
                  new List<PartitionInfo> { new(0, 1, earliest, latest) }));
        return topics;
    }

    [Fact]
    public async Task Consume_from_earliest_starts_at_the_earliest_offset()
    {
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 40L, 10))
            .ReturnsAsync(new[] { new KafkaMessage("orders", 0, 40, "k", "v", DateTime.UnixEpoch) });

        var cmd = new ConsumeCommand(msgs.Object, TopicWithOffsets(40, 100).Object);
        var result = await cmd.ExecuteAsync(Ctx("consume orders --from earliest --limit 10"), CancellationToken.None);

        Assert.IsType<CommandResult.Table>(result);
        msgs.Verify(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 40L, 10), Times.Once);
    }

    [Fact]
    public async Task Consume_from_latest_backs_up_by_the_limit()
    {
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 90L, 10))
            .ReturnsAsync(Array.Empty<KafkaMessage>());

        var cmd = new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 100).Object);
        await cmd.ExecuteAsync(Ctx("consume orders --from latest --limit 10"), CancellationToken.None);

        msgs.Verify(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 0, 90L, 10), Times.Once);
    }

    [Fact]
    public async Task Consume_accepts_an_explicit_numeric_offset()
    {
        var msgs = new Mock<IMessageService>();
        msgs.Setup(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 2, 55L, 5))
            .ReturnsAsync(Array.Empty<KafkaMessage>());

        var cmd = new ConsumeCommand(msgs.Object, TopicWithOffsets(0, 100).Object);
        await cmd.ExecuteAsync(Ctx("consume orders --partition 2 --from 55 --limit 5"), CancellationToken.None);

        msgs.Verify(m => m.FetchMessagesAsync(It.IsAny<IKafkaSession>(), "orders", 2, 55L, 5), Times.Once);
    }

    [Fact]
    public async Task Produce_requires_a_value()
    {
        var cmd = new ProduceCommand(Mock.Of<IMessageService>());

        var result = await cmd.ExecuteAsync(Ctx("produce orders"), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Produce_sends_key_and_value()
    {
        var msgs = new Mock<IMessageService>();
        var cmd = new ProduceCommand(msgs.Object);

        await cmd.ExecuteAsync(Ctx("produce orders --key k1 --value \"hello world\""), CancellationToken.None);

        msgs.Verify(m => m.ProduceAsync(It.IsAny<IKafkaSession>(), "orders", "k1", "hello world"), Times.Once);
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~MessageCommandsTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar**

Criar `Skat.KawkaProject.Tui/Commands/MessageCommands.cs`:

```csharp
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class ConsumeCommand(IMessageService messages, ITopicService topics) : ITuiCommand
{
    public string Name => "consume";
    public string Usage => "consume <topic> [--partition N] [--from earliest|latest|<offset>] [--limit N]";
    public string Summary => "Read messages from one partition";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var session = ctx.RequireSession();
        var topicName = ctx.Parsed.Args[0];
        var partition = ctx.Parsed.IntFlag("partition") ?? 0;
        var limit = ctx.Parsed.IntFlag("limit") ?? 20;
        if (limit < 1) return new CommandResult.Failure("--limit must be at least 1.", ExitCodes.Usage);

        var startOffset = await ResolveStartOffsetAsync(session, topicName, partition, limit, ctx.Parsed.Flag("from"));

        var fetched = await messages.FetchMessagesAsync(session, topicName, partition, startOffset, limit);

        var rows = fetched
            .Select(m => (IReadOnlyList<string>)new[]
            {
                m.Offset.ToString("N0"),
                m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                m.Key ?? "",
                m.Value ?? ""
            })
            .ToList();

        return new CommandResult.Table(
            $"{topicName}[{partition}] from offset {startOffset:N0}",
            new[] { "OFFSET", "TIMESTAMP", "KEY", "VALUE" }, rows);
    }

    /// <summary>Maps --from to a concrete offset. 'latest' means "the last <limit> messages",
    /// which is what someone tailing a topic actually wants.</summary>
    private async Task<long> ResolveStartOffsetAsync(
        IKafkaSession session, string topicName, int partition, int limit, string? from)
    {
        if (from is null or "earliest" or "latest")
        {
            var detail = await topics.GetTopicDetailAsync(session, topicName);
            var info = detail.Partitions.FirstOrDefault(p => p.PartitionId == partition)
                ?? throw new ArgumentOutOfRangeException(nameof(partition), partition,
                    $"Topic '{topicName}' has no partition {partition}.");

            return from == "latest" ? Math.Max(info.EarliestOffset, info.LatestOffset - limit) : info.EarliestOffset;
        }

        if (!long.TryParse(from, out var explicitOffset))
            throw new FormatException($"--from expects 'earliest', 'latest' or a number, got '{from}'.");

        return explicitOffset;
    }
}

public sealed class ProduceCommand(IMessageService messages) : ITuiCommand
{
    public string Name => "produce";
    public string Usage => "produce <topic> [--key K] --value V";
    public string Summary => "Publish a message to a topic";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var value = ctx.Parsed.Flag("value");
        if (value is null)
            return new CommandResult.Failure($"Missing --value. Usage: {Usage}", ExitCodes.Usage);

        var topicName = ctx.Parsed.Args[0];
        await messages.ProduceAsync(ctx.RequireSession(), topicName, ctx.Parsed.Flag("key"), value);

        return new CommandResult.Text($"Produced 1 message to '{topicName}'.");
    }
}
```

Registrar em `Program.cs`:

```csharp
services.AddSingleton<ITuiCommand, ConsumeCommand>();
services.AddSingleton<ITuiCommand, ProduceCommand>();
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet build && dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui Skat.KawkaProject.Tui.Tests/MessageCommandsTests.cs
git commit -m "feat(tui): add consume and produce commands"
```

---

# FASE 4 — Operações destrutivas

> **PRÉ-REQUISITO:** Tasks 1-4 **e Task 9** de `2026-07-24-topic-recreate-hardening.md` concluídas — equivalente às Fases 1-3 daquele plano. As Tasks 1-4 entregam a validação no serviço e a `TopicRecreateFailedException`; a Task 9 entrega o rename para `DeleteAndRecreateTopicAsync`, que o código abaixo chama literalmente. Sem a Task 9 este código não compila. Não comece antes.
>
> **Assinatura de `DeleteAndRecreateTopicAsync`:** 3 parâmetros — `(IKafkaSession session, string topicName, int newPartitionCount)`. O `replicationFactor` **não** é passado pelo chamador: a saga o deriva do tópico vivo (o follow-up de hardening fechou o bug de RF stale). O `RecreateCommand` abaixo, portanto, não lê nem repassa o RF, e a mensagem de sucesso não o menciona.

### Task 11: Confirmadores

Os testes desta task são os mais importantes do projeto: são eles que garantem que um script não apague um tópico por acidente.

**Files:**
- Create: `Skat.KawkaProject.Tui/Safety/InteractiveConfirmer.cs`, `NonInteractiveConfirmer.cs`
- Modify: `Skat.KawkaProject.Tui/Program.cs` (substituir o `NotYetImplementedConfirmer`)
- Test: `Skat.KawkaProject.Tui.Tests/ConfirmerTests.cs`

**Interfaces:**
- Consumes: `IConfirmer`, `DestructiveAction` (Task 3).
- Produces: `InteractiveConfirmer(IAnsiConsole console, Func<string?> readLine)`, `NonInteractiveConfirmer(bool acknowledged, IAnsiConsole console)`.

- [ ] **Step 1: Escrever os testes que falham**

Criar `Skat.KawkaProject.Tui.Tests/ConfirmerTests.cs`:

```csharp
using Spectre.Console;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class ConfirmerTests
{
    private static IAnsiConsole Silent() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(new StringWriter())
    });

    // A factory canônica do Core, não uma lista local: é ela que os comandos usam.
    private static DestructiveAction Recreate(string topic = "orders") =>
        DestructiveAction.Recreate(topic);

    [Fact]
    public async Task Interactive_accepts_only_the_exact_topic_name()
    {
        var confirmer = new InteractiveConfirmer(Silent(), () => "orders");

        Assert.True(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }

    [Theory]
    [InlineData("Orders")]
    [InlineData("order")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Interactive_rejects_anything_else(string? typed)
    {
        var confirmer = new InteractiveConfirmer(Silent(), () => typed);

        Assert.False(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }

    [Fact]
    public async Task NonInteractive_refuses_by_default()
    {
        var confirmer = new NonInteractiveConfirmer(acknowledged: false, Silent());

        Assert.False(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }

    [Fact]
    public async Task NonInteractive_proceeds_only_with_the_explicit_flag()
    {
        var confirmer = new NonInteractiveConfirmer(acknowledged: true, Silent());

        Assert.True(await confirmer.ConfirmAsync(Recreate(), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~ConfirmerTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar os confirmadores**

`Skat.KawkaProject.Tui/Safety/InteractiveConfirmer.cs`:

```csharp
using Spectre.Console;

namespace Skat.KawkaProject.Tui.Safety;

/// <summary>
/// Mirrors the GUI's type-the-name gate. One attempt: a mismatch aborts the command rather than
/// re-prompting, so a mistyped name never turns into "try again until it works".
/// </summary>
public sealed class InteractiveConfirmer(IAnsiConsole console, Func<string?> readLine) : IConfirmer
{
    public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct)
    {
        console.Write(new Panel(new Markup(string.Join('\n', new[]
            {
                $"[bold red]This will {Markup.Escape(action.Verb)} '{Markup.Escape(action.TopicName)}'. It cannot be undone.[/]",
                "",
                "[red]Permanently lost:[/]"
            }
            .Concat(action.WhatIsLost.Select(w => $"  • {Markup.Escape(w)}"))
            // WhatIsPreserved is not decoration: a prompt that lists only the losses sends the
            // operator to re-apply config the saga already carried over. The TUI has no headline of
            // its own, so it renders the canonical lists whole - both halves.
            .Concat(action.WhatIsPreserved.Count == 0 ? Array.Empty<string>() : new[]
            {
                "",
                "[green]Preserved:[/]"
            }.Concat(action.WhatIsPreserved.Select(w => $"  • {Markup.Escape(w)}"))))))
        {
            Border = BoxBorder.Heavy,
            BorderStyle = new Style(Color.Red),
            Header = new PanelHeader(" DESTRUCTIVE ")
        });

        console.Markup($"Type [bold]{Markup.Escape(action.TopicName)}[/] to confirm: ");
        var typed = readLine();

        var ok = string.Equals(typed, action.TopicName, StringComparison.Ordinal);
        if (!ok) console.MarkupLine("[yellow]Name did not match — aborted.[/]");
        return Task.FromResult(ok);
    }
}
```

`Skat.KawkaProject.Tui/Safety/NonInteractiveConfirmer.cs`:

```csharp
using Spectre.Console;

namespace Skat.KawkaProject.Tui.Safety;

/// <summary>
/// Used in one-shot mode and whenever there is no TTY. Refuses by default: with no human to type
/// the topic name, the safe answer is no. A script must state its intent explicitly, which is why
/// the flag is deliberately long and ugly.
/// </summary>
public sealed class NonInteractiveConfirmer(bool acknowledged, IAnsiConsole console) : IConfirmer
{
    public const string AcknowledgeFlag = "yes-i-know-this-deletes-data";

    public Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct)
    {
        if (!acknowledged)
        {
            console.MarkupLine(
                $"[red]Refusing to {Markup.Escape(action.Verb)} '{Markup.Escape(action.TopicName)}' " +
                $"without confirmation.[/] Re-run with [bold]--{AcknowledgeFlag}[/] if you are sure. " +
                $"This would permanently lose: {Markup.Escape(string.Join(", ", action.WhatIsLost))}.");
        }
        return Task.FromResult(acknowledged);
    }
}
```

Em `Program.cs`, remover `NotYetImplementedConfirmer` e substituir o registro por:

```csharp
services.AddSingleton<IConfirmer>(_ => oneShot || wantsPlain
    ? new NonInteractiveConfirmer(parsed.HasFlag(NonInteractiveConfirmer.AcknowledgeFlag), AnsiConsole.Console)
    : new InteractiveConfirmer(AnsiConsole.Console, Console.ReadLine));
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet build && dotnet test Skat.KawkaProject.Tui.Tests`
Expected: PASS, 7 casos novos (o `Theory` contribui 4).

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Tui/Safety Skat.KawkaProject.Tui.Tests/ConfirmerTests.cs Skat.KawkaProject.Tui/Program.cs
git commit -m "feat(tui): add interactive and non-interactive destructive-action confirmers"
```

---

### Task 12: Comandos administrativos de tópico

**Files:**
- Create: `Skat.KawkaProject.Tui/Commands/TopicAdminCommands.cs`
- Modify: `Skat.KawkaProject.Tui/Program.cs`
- Test: `Skat.KawkaProject.Tui.Tests/TopicAdminCommandsTests.cs`

**Interfaces:**
- Consumes: `ITopicService.CreateTopicAsync`, `DeleteTopicAsync`, `ExpandPartitionsAsync`, `DeleteAndRecreateTopicAsync` (nome pós-hardening Task 9); `TopicRecreateFailedException` (hardening Task 4); `IConfirmer` (Task 11).
- Produces: `CreateCommand`, `DeleteCommand`, `IncreaseCommand`, `RecreateCommand`.

- [ ] **Step 1: Escrever os testes que falham**

Criar `Skat.KawkaProject.Tui.Tests/TopicAdminCommandsTests.cs`:

```csharp
using Moq;
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class TopicAdminCommandsTests
{
    private sealed class FixedConfirmer(bool answer) : IConfirmer
    {
        public DestructiveAction? Seen { get; private set; }
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct)
        {
            Seen = a;
            return Task.FromResult(answer);
        }
    }

    private static CommandContext Ctx(string line, IConfirmer confirmer) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = Mock.Of<IKafkaSession>(),
        Confirmer = confirmer
    };

    [Fact]
    public async Task Delete_does_nothing_when_confirmation_is_refused()
    {
        var svc = new Mock<ITopicService>();
        var confirmer = new FixedConfirmer(false);

        var result = await new DeleteCommand(svc.Object).ExecuteAsync(Ctx("delete orders", confirmer), CancellationToken.None);

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(ExitCodes.ConfirmationRefused, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Delete_proceeds_when_confirmed()
    {
        var svc = new Mock<ITopicService>();

        await new DeleteCommand(svc.Object).ExecuteAsync(Ctx("delete orders", new FixedConfirmer(true)), CancellationToken.None);

        svc.Verify(s => s.DeleteTopicAsync(It.IsAny<IKafkaSession>(), "orders"), Times.Once);
    }

    [Fact]
    public async Task Recreate_tells_the_confirmer_everything_that_will_be_lost()
    {
        var svc = new Mock<ITopicService>();
        var confirmer = new FixedConfirmer(false);

        await new RecreateCommand(svc.Object).ExecuteAsync(Ctx("recreate orders --to 2", confirmer), CancellationToken.None);

        Assert.NotNull(confirmer.Seen);
        Assert.Contains(confirmer.Seen!.WhatIsLost, w => w.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(confirmer.Seen.WhatIsLost, w => w.Contains("offset", StringComparison.OrdinalIgnoreCase));
        // NÃO assere ACL: ACLs literais no mesmo nome sobrevivem ao delete+recreate, e a lista
        // canônica do Core as exclui de propósito (ver DestructiveActionTests).
    }

    [Fact]
    public async Task Recreate_surfaces_the_preserved_config_when_the_topic_may_be_gone()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 3),
               new List<PartitionInfo> { new(0, 1, 0, 0) }));
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .ThrowsAsync(new TopicRecreateFailedException(
               TopicRecreateStage.Creating,
               new Dictionary<string, string> { ["retention.ms"] = "604800000" },
               "could not recreate", new InvalidOperationException("broker down")));

        var result = await new RecreateCommand(svc.Object)
            .ExecuteAsync(Ctx("recreate orders --to 2", new FixedConfirmer(true)), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        // The terminal scrollback is the user's only record of the destroyed topic's config.
        Assert.Contains("retention.ms=604800000", failure.Message);
        Assert.Contains("DATA LOSS", failure.Message);
    }

    [Fact]
    public async Task Increase_requires_the_to_flag()
    {
        var result = await new IncreaseCommand(Mock.Of<ITopicService>())
            .ExecuteAsync(Ctx("increase orders", new FixedConfirmer(true)), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, Assert.IsType<CommandResult.Failure>(result).ExitCode);
    }

    [Fact]
    public async Task Create_passes_partitions_and_replication()
    {
        var svc = new Mock<ITopicService>();

        await new CreateCommand(svc.Object).ExecuteAsync(
            Ctx("create orders --partitions 4 --replication 3", new FixedConfirmer(true)), CancellationToken.None);

        svc.Verify(s => s.CreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 4, (short)3), Times.Once);
    }
}
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test Skat.KawkaProject.Tui.Tests --filter "FullyQualifiedName~TopicAdminCommandsTests"`
Expected: FAIL com erro de compilação.

- [ ] **Step 3: Implementar**

Criar `Skat.KawkaProject.Tui/Commands/TopicAdminCommands.cs`:

```csharp
using Skat.KawkaProject.Core.Exceptions;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Commands;

public sealed class CreateCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "create";
    public string Usage => "create <topic> --partitions N [--replication N]";
    public string Summary => "Create a topic";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var partitions = ctx.Parsed.IntFlag("partitions");
        if (partitions is null)
            return new CommandResult.Failure($"Missing --partitions. Usage: {Usage}", ExitCodes.Usage);

        var replication = (short)(ctx.Parsed.IntFlag("replication") ?? 1);
        var topicName = ctx.Parsed.Args[0];

        await topics.CreateTopicAsync(ctx.RequireSession(), topicName, partitions.Value, replication);
        return new CommandResult.Text($"Created '{topicName}' with {partitions} partitions, RF {replication}.");
    }
}

public sealed class DeleteCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "delete";
    public string Usage => "delete <topic>";
    public string Summary => "Delete a topic (destructive)";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var topicName = ctx.Parsed.Args[0];
        // ACLs ficam FORA da lista: ACLs literais no mesmo nome sobrevivem ao delete. Quando esta
        // fase for implementada, promover esta lista a uma factory `DestructiveAction.Delete` no
        // Core, ao lado de `Recreate` - uma lista local aqui reabre a divergência que a
        // centralização de 2026-07-25 fechou.
        var action = new DestructiveAction(topicName, "delete", new[]
        {
            "all messages in the topic",
            "committed consumer group offsets for the topic"
        }, Array.Empty<string>());

        if (!await ctx.Confirmer.ConfirmAsync(action, ct))
            return new CommandResult.Failure($"Aborted: '{topicName}' was not deleted.", ExitCodes.ConfirmationRefused);

        await topics.DeleteTopicAsync(ctx.RequireSession(), topicName);
        return new CommandResult.Text($"Deleted '{topicName}'.");
    }
}

public sealed class IncreaseCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "increase";
    public string Usage => "increase <topic> --to N";
    public string Summary => "Increase a topic's partition count (non-destructive)";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var target = ctx.Parsed.IntFlag("to");
        if (target is null)
            return new CommandResult.Failure($"Missing --to. Usage: {Usage}", ExitCodes.Usage);

        var topicName = ctx.Parsed.Args[0];
        await topics.ExpandPartitionsAsync(ctx.RequireSession(), topicName, target.Value);
        return new CommandResult.Text($"'{topicName}' now has {target} partitions.");
    }
}

public sealed class RecreateCommand(ITopicService topics) : ITuiCommand
{
    public string Name => "recreate";
    public string Usage => "recreate <topic> --to N";
    public string Summary => "Delete and recreate a topic with fewer partitions (destructive)";
    public bool RequiresSession => true;

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct)
    {
        if (ctx.Parsed.Args.Count == 0)
            return new CommandResult.Failure($"Missing topic name. Usage: {Usage}", ExitCodes.Usage);

        var target = ctx.Parsed.IntFlag("to");
        if (target is null)
            return new CommandResult.Failure($"Missing --to. Usage: {Usage}", ExitCodes.Usage);

        var session = ctx.RequireSession();
        var topicName = ctx.Parsed.Args[0];

        // A lista canônica do Core, não uma cópia local: DestructiveAction.Recreate é a mesma
        // fonte que o painel de aviso do GUI lê.
        var action = DestructiveAction.Recreate(topicName);

        if (!await ctx.Confirmer.ConfirmAsync(action, ct))
            return new CommandResult.Failure($"Aborted: '{topicName}' was not modified.", ExitCodes.ConfirmationRefused);

        try
        {
            // No replication factor is passed: the service derives it from the live topic, so a
            // reassignment completing between here and the recreate cannot silently rebuild the
            // topic with a stale factor.
            await topics.DeleteAndRecreateTopicAsync(session, topicName, target.Value);
            return new CommandResult.Text($"'{topicName}' recreated with {target} partitions.");
        }
        catch (TopicRecreateFailedException ex) when (ex.TopicMayBeDeleted)
        {
            // Deletion is asynchronous and irrevocable once issued, so any failure at or after that
            // point is potential data loss. The preserved config goes into the message because the
            // scrollback is the only record the user has left of how the topic was configured.
            var config = ex.PreservedConfig.Count > 0
                ? string.Join(", ", ex.PreservedConfig.Select(kv => $"{kv.Key}={kv.Value}"))
                : "(none)";

            return new CommandResult.Failure(
                $"DATA LOSS RISK: deletion of '{topicName}' was already issued and cannot be undone, but the " +
                $"topic could not be recreated: {ex.InnerException?.Message ?? ex.Message}. " +
                $"Verify it on your cluster and recreate manually if needed. " +
                $"Its config overrides were: {config}",
                ExitCodes.OperationalFailure);
        }
        catch (TopicRecreateFailedException ex)
        {
            return new CommandResult.Failure(
                $"Could not recreate '{topicName}': {ex.InnerException?.Message ?? ex.Message}. " +
                "The topic was NOT modified.", ExitCodes.OperationalFailure);
        }
    }
}
```

Registrar em `Program.cs`:

```csharp
services.AddSingleton<ITuiCommand, CreateCommand>();
services.AddSingleton<ITuiCommand, DeleteCommand>();
services.AddSingleton<ITuiCommand, IncreaseCommand>();
services.AddSingleton<ITuiCommand, RecreateCommand>();
```

- [ ] **Step 4: Rodar a suíte completa**

Run: `dotnet build && dotnet test`
Expected: PASS, incluindo as suítes existentes de `Features.Tests`, `Kafka.Tests` e `Core.Tests`.

- [ ] **Step 5: Verificar a recusa não-interativa de verdade**

```bash
cd /mnt/d/dev/Skat/kawka_project/src
dotnet run --project Skat.KawkaProject.Tui -- delete some-topic --profile local; echo "exit=$?"
```
Expected: recusa, mensagem citando `--yes-i-know-this-deletes-data`, `exit=3`. **Se isto retornar 0 ou deletar o tópico, pare e corrija antes de commitar** — é a garantia central desta fase.

- [ ] **Step 6: Commit**

```bash
git add Skat.KawkaProject.Tui Skat.KawkaProject.Tui.Tests/TopicAdminCommandsTests.cs
git commit -m "feat(tui): add create, delete, increase and recreate commands behind confirmation"
```

---

## Verificação final

- [ ] `dotnet build` → `0 Error(s)`
- [ ] `dotnet test` → todas as suítes verdes (Docker rodando para as de integração)
- [ ] `dotnet run --project Skat.KawkaProject.Tui` → REPL abre com a caixa; `help`, `profiles`, `connect`, `topics`, `describe` funcionam
- [ ] `dotnet run --project Skat.KawkaProject.Tui -- topics --profile local | cat` → saída TSV, sem ANSI, sem bordas
- [ ] `dotnet run --project Skat.KawkaProject.Tui -- delete x --profile local` → recusa com exit 3
- [ ] Nenhum arquivo fora de `Rendering/`, `Input/` e `Safety/` referencia `Console` ou `AnsiConsole`:
      `grep -rn "AnsiConsole\|System.Console\|Console\." Skat.KawkaProject.Tui/Commands/` deve retornar vazio

## Revisão final do projeto

Com **todas** as tasks das quatro fases concluídas, rodar `/powerpuff-review` **sobre o projeto todo** — não apenas sobre o diff da última task.

O objetivo é diferente do gate por task: o `qa-tester` valida cada entrega isoladamente, enquanto esta revisão procura o que só aparece quando tudo está junto — a superfície de comandos como um conjunto coerente, o acoplamento entre `TuiHost`, dispatcher e ciclo de vida da sessão, e divergências de comportamento entre a TUI e o GUI que nenhuma task viu sozinha. Tratar os achados como entrada para um novo ciclo de plano, não como algo a corrigir às pressas no fim.
