# Recreate Architecture Follow-up Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fechar as cinco dívidas que a revisão final das Powerpuff (Blossom/Bubbles) e o agente `architect` levantaram sobre o caminho de recreate — um bug latente (replication factor stale) e quatro itens de higiene/direção — antes que a TUI planejada as multiplique.

**Architecture:** As mudanças concentram-se na fronteira `TopicsViewModel` (Features.Topics) ↔ `TopicRecreateOperation`/`TopicService` (Kafka) ↔ `ITopicService` (Core). A regra que guia: a saga (`TopicRecreateOperation`) é a autoridade sobre os fatos destrutivos e deve derivá-los de metadata ao vivo, não confiar em snapshots stale da VM; a VM valida só como preview advisory. Higiene física: tirar um enum de UI do Core, dedup de um timeout, e centralizar o dado "o que se perde" numa operação destrutiva.

**Tech Stack:** .NET 10, Avalonia 11.3.9 + ReactiveUI 20.1.1, Confluent.Kafka 2.3.0, xUnit 2.9.3 + Moq 4.20.72 (unit), Testcontainers.Kafka 4.4.0 (integração, exige Docker local).

## Global Constraints

- Target framework `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` em todos os projetos.
- `Skat.KawkaProject.Core` referencia ninguém (é a folha). Nada de UI/framework entra nele.
- `Skat.KawkaProject.Kafka` referencia só `Core`. `TopicRecreateOperation` NÃO pode voltar a depender de `TopicService` — a extração do commit `7e002c8` os desacoplou de propósito (a saga recebe o config-read como delegate).
- Rodar tudo a partir de `/mnt/d/dev/Skat/kawka_project/src`.
- Docker no ar para os testes de integração; se indisponível, rodar só `Features.Tests`/`Core.Tests` e anotar a validação de integração como pendente — não marcar a task concluída em silêncio.
- **HARD GATE (do CLAUDE.md global):** cada task passa pelo agente `qa-tester` antes da próxima começar; achados corrigidos, salvo falso positivo/negativo demonstrado. Ao fim de cada task cujo diff muda comportamento, testar por MUTAÇÃO que o teste novo de fato pega a regressão que a task previne.
- Baseline de testes ao entrar: **94 verdes** (49 Kafka, 41 Features, 4 Core).

## Origem dos achados (rastreabilidade)

| Task | Achado | Fonte | Severidade |
|---|---|---|---|
| 1 | Replication factor stale não revalidado na saga | architect #4 / Blossom #4 | Moderada-alta (bug latente) |
| 2 | `ArgumentOutOfRangeException` crua vaza jargão do .NET no banner | Bubbles #1 | Baixa (UX real) |
| 3 | `TopicsFormMode` (enum de UI) vazou pro Core | architect #1 / Blossom #1 | Baixa (custo cresce) |
| 4 | Dado `WhatIsLost`/`DestructiveAction` sem lar compartilhado | architect #3 / Blossom #2 | Baixa→moderada (antes da TUI) |
| 5 | `MetadataQueryTimeout` duplicado nas duas classes | architect #2 / Blossom #3 | Baixa (latente) |

Ordem escolhida por custo-de-reverter e dependência: o bug primeiro (1), o defeito de UX que vive na mesma fronteira (2), a higiene grátis-agora (3), o dado compartilhado antes da TUI (4), o dedup oportunista (5).

## Não-problemas (decididos, NÃO mexer — do architect)

- **A duplicação da validação de partition-count VM↔saga fica.** A cópia da VM é preview advisory (feedback de faixa sem round-trip); a autoridade é a saga, que revalida ao vivo. Extrair um validador compartilhado acoplaria `Features.Topics` a `Kafka` por uma regra que só é load-bearing na saga. O defeito é só o RF (Task 1), não a duplicação.
- **Os dois mecanismos de confirmação (modal `Interaction` vs digitar-o-nome) ficam.** É gradiente de severidade deliberado. Só o DADO (`WhatIsLost`) é centralizado (Task 4), não o mecanismo.
- **Os timeouts de consumidor único da saga (`DeletionTimeout`, `DeletionPropagationGrace`, `DeletionPollInterval`, e `WatermarkQueryTimeout`) ficam onde estão**, com seus comentários load-bearing. Só o `MetadataQueryTimeout` (o único de fato duplicado) é centralizado (Task 5).

---

## Estrutura de arquivos

**Criar:**
- `Skat.KawkaProject.Kafka/KafkaTimeouts.cs` — `internal static` com o `MetadataQueryTimeout` compartilhado (Task 5).
- `Skat.KawkaProject.Core/Models/DestructiveAction.cs` — record de domínio descrevendo uma ação destrutiva e o que ela perde (Task 4).

**Modificar:**
- `Skat.KawkaProject.Kafka/TopicRecreateOperation.cs` — derivar RF ao vivo (Task 1); usar `KafkaTimeouts` (Task 5).
- `Skat.KawkaProject.Kafka/TopicService.cs` — mover `ReplicationFactorOf` para lar compartilhado no assembly (Task 1); usar `KafkaTimeouts` (Task 5); produzir `DestructiveAction` (Task 4).
- `Skat.KawkaProject.Core/Interfaces/ITopicService.cs` — remover o parâmetro `replicationFactor` do contrato destrutivo (Task 1).
- `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs` — parar de passar RF stale (Task 1); traduzir a exceção do serviço em vez de vazar `ex.Message` cru (Task 2).
- `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml` — consumir `DestructiveAction.WhatIsLost` no aviso (Task 4).
- `Skat.KawkaProject.Core/Models/TopicsFormMode.cs` → mover para `Skat.KawkaProject.Features.Topics/` (Task 3).
- Arquivos de teste correspondentes.

**Dependência cruzada com o plano da TUI:** a Task 1 remove o parâmetro `replicationFactor` de `DeleteAndRecreateTopicAsync`. O plano `2026-07-24-tui-headless.md` (Fase 4, `RecreateCommand`) chama esse método com `replicationFactor`. **Ao executar a Task 1, atualizar aquele plano** para a nova assinatura. A Task 4 idem: o spec da TUI já desenha um `DestructiveAction` próprio — apontá-lo para o tipo do Core.

---

## Task 1: Revalidar o replication factor na saga (fecha o bug latente)

A VM deriva o RF de `SelectedTopicDetail.Topic.ReplicationFactor` (snapshot stale) e passa para `DeleteAndRecreateTopicAsync`; a saga repassa direto para o `TopicSpecification` sem revalidar. Se o RF mudou entre o load do detalhe e o recreate (uma reassignment concluiu), o tópico é reconstruído com o RF antigo — durabilidade muda em silêncio, num caminho destrutivo. A saga já lê a metadata completa em `GetPartitionCountAsync` e `ReplicationFactorOf` já existe; basta derivar o RF de lá em vez de confiar no caller.

**Files:**
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs` (mover `ReplicationFactorOf` para um lar compartilhado do assembly)
- Modify: `Skat.KawkaProject.Kafka/TopicRecreateOperation.cs` (derivar RF ao vivo)
- Modify: `Skat.KawkaProject.Core/Interfaces/ITopicService.cs` (remover o parâmetro)
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs` (parar de passar RF)
- Test: `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`
- Modify (plano irmão): `docs/superpowers/plans/2026-07-24-tui-headless.md`

**Interfaces:**
- Consumes: `TopicRecreateAttempt(string TopicName, int OriginalPartitionCount, int RequestedPartitionCount, short ReplicationFactor, IReadOnlyDictionary<string,string> PreservedConfig)` (inalterado — o RF ainda viaja na exceção para reconstrução manual).
- Produces: `Task DeleteAndRecreateTopicAsync(IKafkaSession session, string topicName, int newPartitionCount)` — SEM `replicationFactor`. A saga deriva o RF da metadata ao vivo.

- [x] **Step 1: Tornar `ReplicationFactorOf` acessível à saga sem re-acoplar**

`ReplicationFactorOf` está `internal static` em `TopicService` (`TopicService.cs:17`). A saga (`TopicRecreateOperation`) precisa dela, mas não pode depender de `TopicService`. Mover para um tipo neutro do assembly. Adicionar ao novo/existente helper — criar `Skat.KawkaProject.Kafka/TopicMetadataFacts.cs`:

```csharp
using Confluent.Kafka;

namespace Skat.KawkaProject.Kafka;

/// <summary>Pure derivations over Kafka topic metadata, shared by the adapter and the recreate
/// saga without either depending on the other.</summary>
internal static class TopicMetadataFacts
{
    /// <summary>
    /// Minimum replica count across partitions, not partition 0's. A non-uniform assignment (an
    /// interrupted reassignment) would otherwise report partition 0's factor. DefaultIfEmpty(0)
    /// avoids IndexOutOfRange for a topic reporting no partitions.
    /// </summary>
    public static short ReplicationFactorOf(IEnumerable<int> replicaCountsPerPartition) =>
        (short)replicaCountsPerPartition.DefaultIfEmpty(0).Min();
}
```

Em `TopicService.cs`, remover o método `ReplicationFactorOf` (linhas 17-25) e trocar as duas chamadas (`:43`, `:74`) por `TopicMetadataFacts.ReplicationFactorOf(...)`. Em `TopicServiceIntegrationTests.cs` e `ReplicationFactorTests.cs`, trocar `TopicService.ReplicationFactorOf` por `TopicMetadataFacts.ReplicationFactorOf`.

Run: `dotnet build && dotnet test Skat.KawkaProject.Kafka.Tests --filter "FullyQualifiedName~ReplicationFactor"`
Expected: PASS — movimentação pura, mesmos valores.

- [x] **Step 2: Escrever o teste de integração que falha**

Adicionar a `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`. O teste cria um tópico com RF 1, e verifica que o recreate preserva o RF ao vivo — mesmo que a assinatura não receba mais RF do caller:

```csharp
    [Fact]
    public async Task DeleteAndRecreateTopicAsync_derives_replication_factor_from_the_live_topic()
    {
        using var session = Session();
        var svc = new TopicService();
        await svc.CreateTopicAsync(session, "rf-live", 4, 1);

        // Assinatura nova: sem replicationFactor. A saga tem de derivar RF=1 do tópico vivo.
        await svc.DeleteAndRecreateTopicAsync(session, "rf-live", 2);

        var detail = await svc.GetTopicDetailAsync(session, "rf-live");
        Assert.Equal(2, detail.Partitions.Count);
        Assert.Equal((short)1, detail.Topic.ReplicationFactor);
    }
```

Run: `dotnet build`
Expected: FALHA DE COMPILAÇÃO — `DeleteAndRecreateTopicAsync` ainda exige 4 argumentos. Esperado; a assinatura muda no Step 3.

- [x] **Step 3: Remover o parâmetro do contrato e derivar o RF na saga**

Em `Skat.KawkaProject.Core/Interfaces/ITopicService.cs`, trocar a assinatura (linha 40) para:

```csharp
    Task DeleteAndRecreateTopicAsync(IKafkaSession session, string topicName, int newPartitionCount);
```

Atualizar o doc-comment: remover a menção a `replicationFactor` fora de faixa como causa de `ArgumentOutOfRangeException` (o RF não é mais argumento) e acrescentar uma frase: "The replication factor is derived from the live topic; a non-uniform assignment is flattened to its minimum."

Em `Skat.KawkaProject.Kafka/TopicRecreateOperation.cs`, mudar `GetPartitionCountAsync` para devolver contagem **e** RF, e `ExecuteAsync` para usar o RF derivado. Substituir a assinatura de `GetPartitionCountAsync` e seu retorno:

```csharp
    private static async Task<(int PartitionCount, short ReplicationFactor)> GetTopicFactsAsync(
        IAdminClient admin, string topicName)
    {
        // MetadataQueryTimeout: a constante que TopicRecreateOperation já tem hoje. A Task 5
        // troca-a por KafkaTimeouts.MetadataQueryTimeout - NÃO use KafkaTimeouts aqui, ele ainda
        // não existe nesta task.
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);
        var topic = meta.Topics.FirstOrDefault(t => t.Topic == topicName);

        if (topic is null || topic.Error.Code == ErrorCode.UnknownTopicOrPart)
            throw new InvalidOperationException(
                $"Topic '{topicName}' was not found on the cluster; refusing to recreate it.");

        if (topic.Error.IsError)
            throw new InvalidOperationException(
                $"Could not read metadata for topic '{topicName}': {topic.Error.Reason}. " +
                "Refusing to recreate it until the cluster answers reliably.");

        if (topic.Partitions.Count == 0)
            throw new InvalidOperationException(
                $"Topic '{topicName}' reported no partitions; refusing to recreate it.");

        // .Replicas é int[] (mesmo acesso que ListTopicsAsync/GetTopicDetailAsync já usam: .Length).
        return (topic.Partitions.Count, TopicMetadataFacts.ReplicationFactorOf(
            topic.Partitions.Select(p => p.Replicas.Length)));
    }
```

Em `ExecuteAsync`, trocar a assinatura para receber só `newPartitionCount` (sem `replicationFactor`) e o começo para:

```csharp
    public static async Task ExecuteAsync(
        IAdminClient admin,
        Func<Task<IReadOnlyDictionary<string, string>>> readConfigOverrides,
        string topicName, int newPartitionCount)
    {
        var (currentCount, replicationFactor) = await GetTopicFactsAsync(admin, topicName).ConfigureAwait(false);
        // ... resto idêntico: guarda currentCount<=1, guarda de range, config read, RunRecreateStagesAsync ...
        // O `attempt` passa a usar o replicationFactor DERIVADO, não um parâmetro.
```

Em `TopicService.cs`, o `DeleteAndRecreateTopicAsync` (linha 128) perde o parâmetro `replicationFactor` e a chamada a `ExecuteAsync` também:

```csharp
    public async Task DeleteAndRecreateTopicAsync(
        IKafkaSession session, string topicName, int newPartitionCount)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await TopicRecreateOperation.ExecuteAsync(
            admin,
            () => GetTopicConfigOverridesAsync(session, topicName),
            topicName, newPartitionCount).ConfigureAwait(false);
    }
```

- [x] **Step 4: Atualizar a VM para não passar RF stale**

Em `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, no `RecreateTopicAsync`:
- remover a linha `var replicationFactor = SelectedTopicDetail.Topic.ReplicationFactor;` (`:200`);
- trocar a chamada por `await _topicService.DeleteAndRecreateTopicAsync(_session, topicName, requestedCount);`.

- [x] **Step 5: Atualizar todos os mocks/testes que referenciam a assinatura antiga**

Run: `dotnet build 2>&1 | grep -E "error CS"`
Expected: lista dos call sites de teste com 4 args. Em `TopicsViewModelTests.cs` e `TopicServiceIntegrationTests.cs`, remover o quarto argumento (`(short)N`) de cada `Setup`/`Verify`/chamada de `DeleteAndRecreateTopicAsync`. Corrigir até o build limpar.

- [x] **Step 6: Rodar os testes**

Run: `dotnet test Skat.KawkaProject.Kafka.Tests --filter "FullyQualifiedName~DeleteAndRecreate" && dotnet test Skat.KawkaProject.Features.Tests`
Expected: PASS, incluindo `..._derives_replication_factor_from_the_live_topic`.

- [x] **Step 7: Verificar por mutação que o teste pega o bug**

Mutar `GetTopicFactsAsync` para devolver um RF fixo errado (ex.: `return (topic.Partitions.Count, (short)9);`), rodar `..._derives_replication_factor_from_the_live_topic`. Expected: FALHA (RF esperado 1, obtido 9). Reverter.

- [x] **Step 8: Atualizar o plano irmão da TUI**

Em `docs/superpowers/plans/2026-07-24-tui-headless.md`, na Fase 4 (`RecreateCommand`): a chamada `DeleteAndRecreateTopicAsync(session, topicName, target.Value, replicationFactor)` e o mock `(..., "orders", 2, (short)3)` perdem o quarto argumento. Ajustar o texto do plano e a nota de pré-requisito para a assinatura de 3 parâmetros.

- [x] **Step 9: Rodar a suíte completa e commitar**

Run: `dotnet build && dotnet test`
Expected: PASS.

```bash
git add -A
git commit -m "fix(kafka): derive replication factor from the live topic on recreate"
```

---

## Task 2: Traduzir a exceção do serviço em vez de vazar jargão do .NET no banner

Quando a guarda de faixa da VM passa (contagem stale) mas a do serviço reprova, o serviço lança `ArgumentOutOfRangeException`, que NÃO é `TopicRecreateFailedException` e cai no `catch (Exception ex) { ErrorMessage = ex.Message; }` — despejando no banner vermelho `"...only reduces the partition count. (Parameter 'newPartitionCount') Actual value was 4."`. O `(Parameter ...)` e `Actual value was` são rabo de framework, ao lado de mensagens que o resto do app escreve com cuidado.

**Files:**
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Test: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: `DeleteAndRecreateTopicAsync(session, topicName, newPartitionCount)` (pós-Task 1); lança `ArgumentOutOfRangeException` / `InvalidOperationException` pré-delete, `TopicRecreateFailedException` pós-delete.
- Produces: nenhum tipo novo — só o tratamento no `catch`.

- [x] **Step 1: Escrever o teste que falha**

Adicionar a `TopicsViewModelTests.cs`. Cenário: contagem stale na VM (o painel diz 4 partições) mas o serviço reprova com `ArgumentOutOfRangeException` (como se o tópico tivesse encolhido para 2 no cluster). O banner NÃO deve conter jargão do .NET:

```csharp
    [Fact]
    public async Task A_pre_delete_argument_error_from_the_service_is_shown_without_dotnet_jargon()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 4, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) }));
        svc.Setup(s => s.DeleteAndRecreateTopicAsync(It.IsAny<IKafkaSession>(), "orders", 2))
           .ThrowsAsync(new ArgumentOutOfRangeException("newPartitionCount", 2,
               "Must be between 1 and 1: topic 'orders' currently has 2 partitions, and this operation only reduces the partition count."));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.ShowRecreateFormCommand.Execute(null);
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        Assert.Contains("Must be between 1 and 1", vm.ErrorMessage);
        Assert.DoesNotContain("Parameter", vm.ErrorMessage);
        Assert.DoesNotContain("Actual value was", vm.ErrorMessage);
        Assert.DoesNotContain("DATA LOSS RISK", vm.ErrorMessage);   // nada foi deletado
    }
```

Run: `dotnet test Skat.KawkaProject.Features.Tests --filter "FullyQualifiedName~without_dotnet_jargon"`
Expected: FALHA — hoje o `ex.Message` cru contém "(Parameter 'newPartitionCount')" e "Actual value was 2.".

- [x] **Step 2: Tratar `ArgumentOutOfRangeException` no catch**

Em `TopicsViewModelTests.cs` (na verdade `TopicsViewModel.cs`), no `RecreateTopicAsync`, adicionar um catch específico ANTES do `catch (Exception)` genérico, usando `ex.Message` sem o rabo que o .NET anexa. `ArgumentOutOfRangeException.Message` concatena a mensagem base + "(Parameter ...)" + "Actual value was ...". O texto limpo é a propriedade... na verdade não há propriedade só-mensagem; a convenção é usar a primeira linha. Adicionar helper:

```csharp
        catch (ArgumentException ex)
        {
            // ArgumentException.Message appends "(Parameter 'x')" and ArgumentOutOfRangeException
            // also "Actual value was N." - framework tails that do not belong in a user banner.
            // The message before the first newline is the human sentence we wrote in the service.
            ErrorMessage = ex.Message.Split('\n')[0].Trim();
        }
```

Colocar esse catch entre o `catch (TopicRecreateFailedException ex)` e o `catch (Exception ex)`.

> Nota: `ArgumentException` cobre `ArgumentOutOfRangeException` (subclasse). O `.Message` do .NET põe o "(Parameter...)" na MESMA linha e o "Actual value was" numa linha seguinte para `ArgumentOutOfRangeException`. Se o Split não bastar para remover "(Parameter...)", cortar também nele:

```csharp
        catch (ArgumentException ex)
        {
            var firstLine = ex.Message.Split('\n')[0];
            var paren = firstLine.LastIndexOf(" (Parameter", StringComparison.Ordinal);
            ErrorMessage = (paren >= 0 ? firstLine[..paren] : firstLine).Trim();
        }
```

Use esta segunda forma — remove tanto o "(Parameter...)" da primeira linha quanto o "Actual value was" da segunda.

- [x] **Step 3: Rodar o teste**

Run: `dotnet test Skat.KawkaProject.Features.Tests --filter "FullyQualifiedName~without_dotnet_jargon"`
Expected: PASS.

- [x] **Step 4: Verificar por mutação**

Remover o `catch (ArgumentException ...)` (deixando cair no genérico). Rodar o teste. Expected: FALHA (jargão volta). Reverter.

- [x] **Step 5: Rodar a suíte e commitar**

Run: `dotnet test Skat.KawkaProject.Features.Tests`
Expected: PASS.

```bash
git add src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs src/Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "fix(topics): strip .NET argument-exception tails from the error banner"
```

> **Dívida registrada no gate de QA da Task 2 (não corrigida — fora do escopo):** `ExpandPartitionsAsync`, `DeleteTopicAsync` e `CreateTopicAsync` na mesma VM continuam com `catch (Exception ex) { ErrorMessage = ex.Message; }` puro. O QA verificou que hoje esses fluxos só delegam ao `AdminClient`, cujas falhas de validação chegam como `CreateTopicsException`/`DeleteTopicsException` do broker — não `ArgumentException` client-side —, então o jargão não é atingível por ali. Fica como inconsistência de UX a fechar se algum desses caminhos passar a validar argumentos localmente.

---

## Task 3: Mover `TopicsFormMode` do Core para Features.Topics

Enum de estado de UI ("which inline form is open in the topics detail panel") mora em `Core.Models`, o assembly-folha que todos referenciam. Consumidor único: `TopicsViewModel`. Sujar o Core força rebuild em cascata de Kafka + 4 Features + UI por uma mudança de painel, e a TUI planejada (que referencia só o Core) passaria a linkar contra vocabulário de painel Avalonia. Move Class puro.

**Files:**
- Move: `Skat.KawkaProject.Core/Models/TopicsFormMode.cs` → `Skat.KawkaProject.Features.Topics/TopicsFormMode.cs`
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs` (namespace do enum)

**Interfaces:**
- Consumes/Produces: `enum TopicsFormMode { None, Create, Expand, Recreate }` — mesma forma, novo namespace `Skat.KawkaProject.Features.Topics`.

- [x] **Step 1: Confirmar consumidor único**

Run: `grep -rn 'TopicsFormMode' src --include='*.cs' | grep -v '/obj/'`
Expected: ocorrências só em `Core/Models/TopicsFormMode.cs` e `Features.Topics/ViewModels/TopicsViewModel.cs`. Se aparecer em outro lugar, parar e reavaliar — a premissa "consumidor único" mudou.

- [x] **Step 2: Mover o arquivo e ajustar o namespace**

```bash
git mv src/Skat.KawkaProject.Core/Models/TopicsFormMode.cs src/Skat.KawkaProject.Features.Topics/TopicsFormMode.cs
```

Editar `src/Skat.KawkaProject.Features.Topics/TopicsFormMode.cs`: trocar `namespace Skat.KawkaProject.Core.Models;` por `namespace Skat.KawkaProject.Features.Topics;`. Manter o resto (o doc-comment e o enum) intacto.

- [x] **Step 3: Ajustar o using no VM**

Em `TopicsViewModel.cs`, o `ActiveForm`/`TopicsFormMode` era resolvido via `using Skat.KawkaProject.Core.Models;`. O novo namespace `Skat.KawkaProject.Features.Topics` é o namespace-pai do VM (`...Features.Topics.ViewModels`), então NÃO é auto-visível. Adicionar `using Skat.KawkaProject.Features.Topics;` no topo do `TopicsViewModel.cs`.

> **Correção na execução (2026-07-25):** a premissa acima está errada. Em C#, um namespace aninhado (`...Features.Topics.ViewModels`) enxerga os tipos do namespace-pai (`...Features.Topics`) sem `using`. O build passou com 0 erros sem nenhum using adicional, e ele NÃO foi acrescentado — seria ruído.

Run: `dotnet build 2>&1 | grep -E "error CS"`
Expected: se aparecer "TopicsFormMode não encontrado", faltou o using — adicionar. Corrigir até limpar.

- [x] **Step 4: Rodar a suíte (movimentação pura → tudo verde sem tocar asserção)**

Run: `dotnet build && dotnet test`
Expected: PASS, 94 verdes, nenhuma asserção alterada. Se algum teste referenciava `Core.Models.TopicsFormMode`, ajustar o using dele também (grep do Step 1 já teria mostrado).

- [x] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(topics): move TopicsFormMode out of Core into Features.Topics"
```

---

## Task 4: Centralizar `DestructiveAction`/`WhatIsLost` no Core

A lista "o que se perde numa operação destrutiva" existe em três lugares que já divergem em potencial: o doc-comment de `ITopicService`, o `BuildRecreateFailureMessage` da VM, e o aviso no AXAML — e a TUI planejada (`...tui-headless-design.md`) já desenha um `DestructiveAction(TopicName, Verb, WhatIsLost)` PRÓPRIO. Extrair o DADO (não o mecanismo de confirmação) para o Core evita a quarta cópia.

**Files:**
- Create: `Skat.KawkaProject.Core/Models/DestructiveAction.cs`
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs` (usar a lista canônica)
- Modify: `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml` (o aviso de recreate)
- Test: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`
- Modify (spec irmão): `docs/superpowers/specs/2026-07-24-tui-headless-design.md`

**Interfaces:**
- Produces: `record DestructiveAction(string TopicName, string Verb, IReadOnlyList<string> WhatIsLost)` em `Skat.KawkaProject.Core.Models`, mais uma factory estática canônica `DestructiveAction.Recreate(string topicName)` que enumera o que o recreate perde.

- [x] **Step 1: Escrever o teste da factory**

Criar em `TopicsViewModelTests.cs` (ou um `DestructiveActionTests.cs` em Core.Tests — preferir Core.Tests já que o tipo é do Core):

Criar `Skat.KawkaProject.Core.Tests/DestructiveActionTests.cs`:

```csharp
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Tests;

public class DestructiveActionTests
{
    [Fact]
    public void Recreate_enumerates_what_a_shrink_recreate_destroys()
    {
        var action = DestructiveAction.Recreate("orders");

        Assert.Equal("orders", action.TopicName);
        Assert.Equal("recreate", action.Verb);
        // As três consequências que a revisão confirmou: mensagens, offsets de consumer group,
        // e o que NÃO se perde (config overrides) fica de fora da lista de perdas.
        Assert.Contains(action.WhatIsLost, w => w.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(action.WhatIsLost, w => w.Contains("offset", StringComparison.OrdinalIgnoreCase));
    }
}
```

> Nota: NÃO incluir ACL na lista — a revisão (QA Task 10) demonstrou que a premissa "ACLs somem" contradiz a semântica do Kafka (ACLs literais no mesmo nome persistem). A lista canônica cobre mensagens e offsets, que são verdadeiros.

Run: `dotnet test Skat.KawkaProject.Core.Tests`
Expected: FALHA DE COMPILAÇÃO — `DestructiveAction` não existe.

- [x] **Step 2: Criar o tipo no Core**

Criar `Skat.KawkaProject.Core/Models/DestructiveAction.cs`:

```csharp
namespace Skat.KawkaProject.Core.Models;

/// <summary>
/// Describes a destructive topic operation and what it irreversibly loses. Presentation-agnostic:
/// the GUI renders WhatIsLost in a warning panel, the (planned) TUI in a confirmation prompt, and
/// the recreate failure message reuses it - one canonical list instead of a copy per frontend.
/// HOW the operation is confirmed (modal vs type-the-name) is a per-frontend concern and is NOT
/// modelled here.
/// </summary>
public sealed record DestructiveAction(string TopicName, string Verb, IReadOnlyList<string> WhatIsLost)
{
    /// <summary>What a shrink-by-recreate destroys. ACLs are deliberately excluded: literal ACLs on
    /// the same topic name survive delete+recreate, so claiming they are lost would be wrong.</summary>
    public static DestructiveAction Recreate(string topicName) => new(
        topicName, "recreate", new[]
        {
            "all messages in the topic",
            "committed consumer group offsets (consumers may then silently skip or replay records)"
        });
}
```

Run: `dotnet test Skat.KawkaProject.Core.Tests`
Expected: PASS.

- [x] **Step 3: Consumir a lista no aviso da VM/AXAML**

O aviso hoje é texto fixo no `TopicsView.axaml`. Expor uma propriedade na VM que projeta `DestructiveAction.Recreate(topic).WhatIsLost` como texto e bindar o AXAML nela — ou, mais simples e testável, manter o AXAML e apenas garantir que a mensagem de falha (`BuildRecreateFailureMessage`) e o aviso derivem da MESMA fonte. Passo mínimo que fecha a duplicação sem reescrever o AXAML: em `TopicsViewModel`, adicionar:

```csharp
    // A fonte única do "o que se perde" do recreate; o aviso e a mensagem de falha derivam daqui.
    public string RecreateWhatIsLost =>
        string.Join("; ", DestructiveAction.Recreate(SelectedTopicDetail?.Topic.Name ?? "").WhatIsLost) + ".";
```

E no `TopicsView.axaml`, trocar o segundo TextBlock de aviso (o de offsets, texto fixo) por `Text="{Binding RecreateWhatIsLost}"`. Manter o primeiro TextBlock (o "ALL MESSAGES... cannot be undone" em vermelho) como está — é a manchete de severidade.

> Se preferir não bindar (evitar recalcular por seleção), deixar o AXAML fixo e apenas usar `DestructiveAction.Recreate` dentro de `BuildRecreateFailureMessage`. O objetivo da task é UMA fonte para a lista; escolha o ponto de consumo, mas não deixe duas listas divergirem.

> **Decisões tomadas na execução (2026-07-25), divergindo do desenho acima:**
> 1. **O record carrega `WhatIsPreserved` além de `WhatIsLost`.** O doc-comment do `ITopicService` e o AXAML já mantinham as duas metades juntas ("NOT carried over: … / Carried over: config overrides"). Centralizar só as perdas deixaria "config overrides são preservados" duplicado exatamente nos mesmos dois lugares que a task veio fechar — e um aviso que só lista perdas manda o usuário reaplicar config que a saga já carregou.
> 2. **As listas são expostas como `DestructiveAction.RecreateLoses`/`RecreatePreserves`,** com `Recreate(topicName)` montando o record a partir delas. Um consumidor que só quer o texto (o painel de aviso) lê as listas sem inventar um nome de tópico fake; um consumidor que age sobre um tópico (a TUI) usa a factory.
> 3. **A VM expõe duas propriedades (`RecreateWhatIsLost`, `RecreateWhatIsPreserved`) e o AXAML tem dois TextBlocks.** São constantes da operação, não do tópico selecionado — o texto nunca interpola o nome —, então não precisam de `RaisePropertyChanged`.
> 4. **O doc-comment do `ITopicService` era o terceiro lugar divergente e foi corrigido:** afirmava que ACLs se perdem, o que contradiz a lista canônica e é falso (ACLs literais no mesmo nome sobrevivem). Agora aponta para `DestructiveAction.Recreate` em vez de repetir a lista.

- [x] **Step 4: Rodar build + suíte**

Run: `dotnet build && dotnet test`
Expected: PASS.

- [x] **Step 5: Apontar o spec da TUI para o tipo do Core**

Em `docs/superpowers/specs/2026-07-24-tui-headless-design.md`, na seção de confirmação destrutiva: trocar o desenho de um `DestructiveAction` próprio da TUI por "reusa `Skat.KawkaProject.Core.Models.DestructiveAction`; o `IConfirmer` recebe esse record". Ajustar o plano `2026-07-24-tui-headless.md` (Fase 4) na mesma linha.

- [x] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(core): centralize destructive-action 'what is lost' as a shared record"
```

---

## Task 5: `KafkaTimeouts` interno para o `MetadataQueryTimeout` duplicado

`MetadataQueryTimeout = 10s` está definido em `TopicService.cs:10` e `TopicRecreateOperation.cs:20` — o único valor de fato duplicado. Ambos alimentam `admin.GetMetadata(timeout)`. Centralizar num terceiro tipo do assembly `Kafka` (não no Core — é detalhe de implementação da Confluent.Kafka, não contrato de domínio), sem que uma classe dependa da outra.

**Files:**
- Create: `Skat.KawkaProject.Kafka/KafkaTimeouts.cs`
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs`, `Skat.KawkaProject.Kafka/TopicRecreateOperation.cs`

**Interfaces:**
- Produces: `internal static class KafkaTimeouts` com `public static readonly TimeSpan MetadataQueryTimeout = TimeSpan.FromSeconds(10);`.

- [x] **Step 1: Criar o tipo**

Criar `Skat.KawkaProject.Kafka/KafkaTimeouts.cs`:

```csharp
namespace Skat.KawkaProject.Kafka;

/// <summary>
/// Blocking-call timeouts for the Confluent.Kafka AdminClient, shared where the same value is used
/// by more than one class in this assembly. Only MetadataQueryTimeout is here because it is the only
/// one genuinely shared (TopicService and TopicRecreateOperation both feed it to GetMetadata).
/// Single-consumer timeouts (watermark, deletion grace/poll/timeout) stay next to their use, where
/// their comments explain the choice.
/// </summary>
internal static class KafkaTimeouts
{
    public static readonly TimeSpan MetadataQueryTimeout = TimeSpan.FromSeconds(10);
}
```

- [x] **Step 2: Referenciar dos dois lados**

Em `TopicService.cs`: remover `private static readonly TimeSpan MetadataQueryTimeout = TimeSpan.FromSeconds(10);` (linha 10) e trocar os usos (`GetMetadata(MetadataQueryTimeout)`) por `KafkaTimeouts.MetadataQueryTimeout`.

Em `TopicRecreateOperation.cs`: remover `private static readonly TimeSpan MetadataQueryTimeout = TimeSpan.FromSeconds(10);` (linha 20) e trocar todos os usos por `KafkaTimeouts.MetadataQueryTimeout`.

Confirmar que nenhuma das duas classes passou a referenciar a outra:

Run: `grep -n 'TopicService\.' src/Skat.KawkaProject.Kafka/TopicRecreateOperation.cs; grep -n 'TopicRecreateOperation\.' src/Skat.KawkaProject.Kafka/TopicService.cs`
Expected: vazio dos dois lados — ambos dependem só de `KafkaTimeouts`.

- [x] **Step 3: Rodar build + suíte (mesmo valor → tudo verde)**

Run: `dotnet build && dotnet test`
Expected: PASS, sem mudança de comportamento.

- [x] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(kafka): dedup MetadataQueryTimeout into a shared internal KafkaTimeouts"
```

---

## Verificação final

- [x] `dotnet build` → `0 Error(s)`
- [x] `dotnet test` → todos verdes, com Docker rodando para os de integração
- [x] Grafo de referências inalterado: `Core` ainda não referencia ninguém; `TopicRecreateOperation` não referencia `TopicService` nem vice-versa (`grep` do Task 5, Step 2)
- [x] Os dois planos/specs da TUI (`2026-07-24-tui-headless.md`, `2026-07-24-tui-headless-design.md`) refletem a assinatura de 3 parâmetros de `DeleteAndRecreateTopicAsync` (Task 1) e o `DestructiveAction` do Core (Task 4)
- [x] Rodar `/powerpuff-review` sobre o diff deste plano, como fechamento — os cinco achados eram da revisão anterior; confirmar que as correções não introduziram novos

## Fechamento da revisão (2026-07-25)

A revisão de fechamento **encontrou achados novos**, ao contrário do que a linha acima antecipava. Vale registrar porque muda a leitura do gate de QA por task:

**Buttercup achou um bug que os cinco gates de QA com mutação não pegaram** — e não por descuido dos gates: `GetTopicFactsAsync` validava a metadata com três guardas e devolvia sem checar o único número que a função existe para produzir. O RF é o mínimo entre partições, então uma partição com `Replicas` vazio o leva a 0 com todas as guardas passando; esse 0 só vira `TopicSpecification` dentro do retry de create, ou seja **depois do delete**, onde o broker o recusa como erro permanente. Tópico deletado, nada no lugar.

A lição de processo: **os estados que essas guardas recusam são inalcançáveis contra o container single-broker saudável da suíte de integração.** Um gate que valida por integração + mutação não consegue, por construção, exercitar uma guarda cujo gatilho o ambiente de teste não produz. Guardas desse tipo precisam de teste unitário sobre uma função pura — foi para isso que a validação migrou para `TopicMetadataFacts.FactsFor`/`Agreed`.

Corrigido em `0347060` e no commit seguinte:
- guarda de RF < 1 e guarda de erro por partição, ambas antes de qualquer chamada destrutiva;
- **duas leituras de metadata que precisam concordar** (`Agreed`), mesma disciplina que `RequiredConsecutiveAbsences` já aplicava ao delete — fecha a assimetria de o delete exigir duas amostras e a forma do tópico se contentar com uma;
- manchete do painel derivada do Core: era literal no AXAML enquanto a linha de baixo omitia a perda de mensagens supondo que a manchete a declarava;
- `UserFacing` aplicado em todos os catches da VM, fechando a dívida registrada no gate da Task 2 (`ExpandPartitionsAsync`/`DeleteTopicAsync`/`CreateTopicAsync` mostravam `ex.Message` cru).

**Suíte final: 110 verdes** (7 Core, 44 Features, 59 Kafka com Docker).
