# Topic Recreate Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar o caminho de "recriar tópico com menos partições" seguro e honesto — validar antes do ponto sem volta, nunca perder a config, nunca dizer ao usuário que nada aconteceu quando os dados podem ter sumido.

**Architecture:** A validação e a preservação de estado descem para `TopicService`, que passa a lançar uma exceção tipada (`TopicRecreateFailedException`) carregando a etapa em que falhou e a config que tinha em mãos. `TopicsViewModel` deixa de re-derivar o estado do broker e passa a ler essa exceção. A UI ganha travamento durante operações e exclusividade mútua entre os três formulários inline.

**Tech Stack:** .NET 10, Avalonia 11.3.9 + ReactiveUI 20.1.1, Confluent.Kafka 2.3.0, xUnit 2.9.3 + Moq 4.20.72 (unit), Testcontainers.Kafka 4.4.0 (integração, exige Docker local).

## Pendências de verificação registradas (revisões de QA)

- **Smoke test manual da UI** (QA Task 6, OBS-1): `ReselectTopicByName` existe para o caminho em que o `ListBox` escreve `null` de volta em `SelectedTopic` durante `ApplyFilter` — que só ocorre com o Avalonia rodando. Os testes unitários só alcançam a variante *stale*, então esse caminho fica coberto apenas por teste manual: recriar um tópico com sucesso e confirmar que a linha segue destacada no `ListBox` e o painel coerente.
- **Navegação durante operação** (QA Task 6, OBS-2): sem gating na `ListBox`, clicar em outro tópico durante um recreate tem a seleção revertida ao concluir. **Fechado pela Task 7** — `IsEnabled="{Binding IsNotBusy}"` na `ListBox` recusa o clique de seleção durante a operação.
- **Filtro durante operação** (QA Task 7, OBS-3): o `Filter` TextBox NÃO é gated por decisão deliberada — filtrar é exibição, não muta o broker, e travá-lo numa espera de 30s é pior que a consequência. Se o usuário filtrar o tópico selecionado para fora da lista durante um recreate, `ReselectTopicByName` não o reacha e a seleção fica stale (cosmético, auto-corrige na próxima seleção). Mesma classe do smoke test manual acima.
- **Broker com ACL / delete recusado** (QA Tasks 4-5): a classificação `Error.IsLocalError` que decide `TopicMayBeDeleted` não foi exercitada contra ACL real — o container de teste não tem ACLs. Aproximada por outros códigos de erro do broker.
- **Versões de broker** (QA Tasks 2-3): tudo medido contra `cp-kafka:6.1.9` single-node (Kafka 2.7, ZooKeeper). KRaft, Kafka 3.x/4.x e multi-broker não foram medidos.
- **Flakiness da suíte de integração** (QA Tasks 3 e 7): `..._reduces_partitions_and_preserves_config` falhou uma vez com `Local: Timed out` na suíte cheia e passou isolado. Causa: `IAsyncLifetime` na classe de teste sobe um container Kafka por método, e sob carga concorrente o controller do broker single-node estoura. Backlog: `IClassFixture`/`ICollectionFixture` compartilhado cortaria ~2min e a flakiness.

## Global Constraints

- Target framework: `net10.0` em todos os projetos (já configurado — não alterar).
- `Nullable` é `enable` em todos os projetos: anotações de nulabilidade são obrigatórias.
- `Skat.KawkaProject.Features.Topics` NÃO pode ganhar referência de projeto para `Skat.KawkaProject.Features.Messages` — a navegação cruza essa fronteira via callback `Action<string, int>` composto em `Skat.KawkaProject.UI`.
- Seguir os padrões ReactiveUI já presentes em `TopicsViewModel`: `ReactiveCommand.Create`/`CreateFromTask`, tratamento de `IsBusy`/`ErrorMessage` idêntico em forma ao de `CreateTopicAsync`.
- Seguir a linguagem visual de `TopicsView.axaml`: brushes via `DynamicResource` (`SurfaceBrush`, `AccentBrush`, `AccentSubtleBrush`, `DestructiveBrush`, `StatusErrorBrush`, `DestructiveTextBrush`, `TextMutedBrush`, `TextPrimaryBrush`, `BorderBrush`, `BorderSubtleBrush`, `TextFaintBrush`), FontSize 11 para corpo / 10 para rótulos, `Padding="8,4"` em botões.
- Todos os comandos são rodados a partir de `/mnt/d/dev/Skat/kawka_project/src`.
- Testes de integração precisam de Docker rodando. Se Docker não estiver disponível, rode apenas `Skat.KawkaProject.Features.Tests` e anote que a validação de integração ficou pendente — **não** marque a tarefa como concluída silenciosamente.
- Este plano corrige código **já mergeado na main** (PR #1). Não é bloqueio de merge; é hardening de follow-up.

## HARD GATE — revisão obrigatória entre tasks

**Nenhuma task começa antes de a anterior ser revisada pelo agente `qa-tester` e os bugs apontados serem corrigidos.**

Ordem obrigatória, ao fim de cada task deste plano:

1. Implementar a task e rodar os testes indicados nos seus steps.
2. Despachar o agente `qa-tester` sobre o que foi entregue naquela task.
3. Corrigir todo bug apontado.
4. Só então iniciar a próxima task.

**Única exceção:** um achado pode ser dispensado sem correção quando for demonstrado ser **falso positivo** (o problema relatado não existe no código) ou **falso negativo** (o resultado do teste não reflete o comportamento real) — nos dois casos, o resultado do teste não corresponde à realidade. A demonstração precisa citar o trecho de código ou a saída de execução que prova a divergência, registrada na resposta. *"Parece um falso positivo"* não basta.

Não pule o gate porque a task parece pequena ou porque a suíte já está verde. Se o `qa-tester` não puder rodar, pare e reporte — não prossiga assumindo que passaria.

## Rastreabilidade dos achados

| Achado | Persona | Task |
|---|---|---|
| Serviço deleta primeiro e valida nunca | 💚 #4 / 🌸 #2 | 1 |
| `!IsDefault` fixa defaults do broker como override | 💚 #9 | 2 |
| Nome promete config inteira, entrega só overrides | 💙 #1 | 2 |
| Loop de espera trata "não vi" como "sumiu" | 💚 #3 | 3 |
| Durações sem nome; comentário explica o quê, não o porquê | 💙 #6 | 3 |
| Sem `ConfigureAwait(false)` em lib | 💚 #3 | 3 |
| Falha destrói a config permanentemente | 💚 #1 | 4 |
| Erro diz "timed out" enquanto dados evaporam | 💚 #2 | 5 |
| `bool? stillExists` tri-state invisível | 💙 #5 | 5 |
| VM re-deriva fato que o serviço sabia (Feature Envy) | 🌸 #1 | 5 |
| UI segue mostrando tópico inexistente | 💚 #5 | 6 |
| `SelectedTopic == null` com painel aberto → delete nulo | 💚 #10 | 6 |
| Nada desabilitado durante operação destrutiva | 💚 #6 | 7 |
| Campo limpo deixa valor obsoleto | 💚 #11 | 8 |
| Erro "must be between 1 and 0" | 💙 #4 | 8 |
| Método destrutivo sem doc na interface | 💙 #2 / 🌸 #2 | 9 |
| `NewPartitionCount` é o nome genérico do meio | 💙 #3 | 9 |
| Parâmetro `partition` vs `partitionId` | 💙 #3 | 9 |
| Offsets de consumer group corrompidos silenciosamente | 💚 #7 | 10 |
| ACLs descartadas no recreate | 💚 #8 | 10 |
| RF derivado só da partição 0 | 💚 #9b | 10 |
| Formulários não são mutuamente exclusivos | 🌸 #5 | 11 |
| `IsNot*` mortos | 💙 #7 | 11 |
| Limite inferior nunca testado | 💙 #9 | 12 |
| Nomes de teste prometem demais | 💙 #9 | 12 |
| Testes dependem de mock síncrono | 💙 #10 | 12 |
| Saga num adapter fino | 🌸 #1 | 13 (opcional) |
| Composition root dono do init de `MessagesViewModel` | 🌸 #4 | 14 (opcional) |
| Null-check deferido sem comentário | 💙 #8 | 14 (opcional) |
| `GetTopicDetailAsync` bloqueia a UI thread | 💚 #12 | 3 (parcial) + 13 |
| `GetMetadata(topicName, …)` auto-cria o tópico | QA Task 1 (O-4) | 3 (Step 4) |

**Fora de escopo deste plano** (registrado, não endereçado): 🌸 #3 — unificar os dois mecanismos de confirmação (`Interaction` vs digitar-o-nome) e rotear ambos os caminhos destrutivos pela mesma primitiva de delete. É uma decisão de produto sobre qual mecanismo vence, e a Buttercup explicitamente endossou o gate atual. Decidir antes de planejar.

---

## Estrutura de arquivos

**Criar:**
- `Skat.KawkaProject.Core/Exceptions/TopicRecreateFailedException.cs` — exceção tipada com a etapa da falha e a config preservada. Responsabilidade única: transportar "onde quebrou e o que eu tinha na mão" do serviço até a VM.
- `Skat.KawkaProject.Core/Models/TopicsFormMode.cs` — enum de qual formulário inline está aberto.

**Modificar:**
- `Skat.KawkaProject.Core/Interfaces/ITopicService.cs` — renomes + doc comments do contrato destrutivo.
- `Skat.KawkaProject.Kafka/TopicService.cs` — validação, filtro de config, loop de espera, retry, `ConfigureAwait`.
- `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs` — tratamento de erro, sincronização de estado, gating, exclusividade de formulários.
- `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml` — `IsEnabled`, avisos, binding do delete.
- `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs` — cobertura faltante, nomes honestos.
- `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs` — testes de validação e preservação.

---

# FASE 1 — Blindar o serviço

### Task 1: Validar argumentos antes do ponto sem volta

Hoje `RecreateTopicWithFewerPartitionsAsync` não tem nenhuma guarda. Qualquer chamador que passe `0`, negativo, ou um número maior que o atual deleta o tópico e só descobre o problema quando o broker rejeita o create.

**Files:**
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs:88-107`
- Test: `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`

**Interfaces:**
- Consumes: nada de tasks anteriores.
- Produces: `RecreateTopicWithFewerPartitionsAsync` passa a lançar `ArgumentOutOfRangeException` (paramName `newPartitionCount`) para contagem inválida e `InvalidOperationException` para tópico inexistente, **antes** de qualquer chamada destrutiva.

- [ ] **Step 1: Escrever os testes que falham**

Adicionar ao final de `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`, antes do `}` de fechamento da classe:

```csharp
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    public async Task RecreateTopicWithFewerPartitionsAsync_rejects_invalid_count_without_deleting(int requested)
    {
        var svc = new TopicService();
        using var session = Session();
        var topic = $"guard-topic-{requested}";
        await svc.CreateTopicAsync(session, topic, 4, 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, topic, requested, 1));

        var detail = await svc.GetTopicDetailAsync(session, topic);
        Assert.Equal(4, detail.Partitions.Count);
    }

    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_rejects_unknown_topic()
    {
        var svc = new TopicService();
        using var session = Session();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, "no-such-topic-here", 1, 1));
    }
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test Skat.KawkaProject.Kafka.Tests --filter "FullyQualifiedName~RecreateTopicWithFewerPartitionsAsync_rejects"`
Expected: FAIL — os `Theory` falham porque nenhuma exceção é lançada e o tópico é destruído; o teste de tópico desconhecido falha com exceção de tipo diferente.

- [ ] **Step 3: Implementar a validação**

Em `Skat.KawkaProject.Kafka/TopicService.cs`, substituir o corpo inteiro de `RecreateTopicWithFewerPartitionsAsync` (linhas 88-107) por:

```csharp
    public async Task RecreateTopicWithFewerPartitionsAsync(
        IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();

        // Validate BEFORE anything destructive: once DeleteTopicsAsync is issued the
        // deletion is asynchronous and irrevocable, so an invalid argument discovered
        // afterwards costs the user their data.
        var currentCount = await GetPartitionCountAsync(admin, topicName);
        if (newPartitionCount < 1 || newPartitionCount >= currentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newPartitionCount), newPartitionCount,
                $"Must be between 1 and {currentCount - 1}: topic '{topicName}' currently has {currentCount} partitions, " +
                "and this operation only reduces the partition count.");
        }

        var config = await GetTopicConfigAsync(session, topicName);

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

    private static async Task<int> GetPartitionCountAsync(IAdminClient admin, string topicName)
    {
        var meta = await Task.Run(() => admin.GetMetadata(topicName, TimeSpan.FromSeconds(10)));
        var topic = meta.Topics.FirstOrDefault(t => t.Topic == topicName);
        if (topic == null || topic.Error.IsError || topic.Partitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Topic '{topicName}' was not found on the cluster; refusing to recreate it.");
        }
        return topic.Partitions.Count;
    }
```

- [ ] **Step 4: Rodar os testes para confirmar que passam**

Run: `dotnet test Skat.KawkaProject.Kafka.Tests --filter "FullyQualifiedName~RecreateTopicWithFewerPartitions"`
Expected: PASS — inclusive o teste pré-existente `..._reduces_partitions_and_preserves_config` deve continuar verde.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Kafka/TopicService.cs Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs
git commit -m "fix(kafka): validate partition count before deleting topic on recreate"
```

---

### Task 2: Filtrar por `ConfigSource` e renomear para refletir a semântica

`!e.IsDefault` só exclui valores iguais ao default embutido do Kafka. Um valor herdado do `server.properties` do broker reporta `StaticBrokerConfig`, passa no filtro, e é gravado como override explícito do tópico — congelando aquele tópico naquele valor para sempre.

**Files:**
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs:76-86`
- Modify: `Skat.KawkaProject.Core/Interfaces/ITopicService.cs:12`
- Test: `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs:54-77`

**Interfaces:**
- Consumes: nada.
- Produces: `Task<IReadOnlyDictionary<string, string>> GetTopicConfigOverridesAsync(IKafkaSession session, string topicName)` — substitui `GetTopicConfigAsync`. Tasks 1 e 4 chamam este nome.

- [ ] **Step 1: Escrever o teste que falha**

Em `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`, substituir o teste `GetTopicConfigAsync_returns_overridden_config_values` inteiro por:

```csharp
    [Fact]
    public async Task GetTopicConfigOverridesAsync_returns_only_topic_level_overrides()
    {
        var svc = new TopicService();
        using var session = Session();

        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        }).Build();

        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = "config-topic",
                NumPartitions = 1,
                ReplicationFactor = 1,
                Configs = new Dictionary<string, string> { ["retention.ms"] = "604800000" }
            }
        });

        var config = await svc.GetTopicConfigOverridesAsync(session, "config-topic");

        Assert.Equal("604800000", config["retention.ms"]);
    }
```

> **Corrigido após a revisão de QA desta task.** A versão anterior deste passo prescrevia três
> asserções que **não detectariam o bug**, todas verificadas contra broker real:
> `Assert.DoesNotContain("log.retention.hours", …)` e `Assert.DoesNotContain("num.partitions", …)`
> passam com qualquer filtro, inclusive sem filtro nenhum — nenhuma das duas chaves aparece no
> `DescribeConfigs` de um recurso `Topic` (são nomes de config de broker); e `Assert.True(config.Count < 5)`
> passava com o filtro antigo, que devolvia exatamente 2 entradas. O passo teria ficado "verde"
> mantendo o defeito.
>
> A asserção que **de fato** prende o comportamento é a contagem zero num tópico virgem, mais um
> tópico virgem sob config dinâmico de broker. Ambos abaixo.

Adicionar também os dois testes que prendem o vazamento — sem eles, mutações como
`.Where(e => !e.IsDefault && e.Source != ConfigSource.StaticBrokerConfig)` mantêm a suíte inteira
verde e reintroduzem o bug em produção (verificado):

```csharp
    [Fact]
    public async Task GetTopicConfigOverridesAsync_returns_nothing_when_the_topic_overrides_nothing()
    {
        using var session = Session();
        var svc = new TopicService();
        await svc.CreateTopicAsync(session, "plain-topic", 1, 1);

        var config = await svc.GetTopicConfigOverridesAsync(session, "plain-topic");

        Assert.True(config.Count == 0,
            $"Expected no overrides, got {config.Count}: {string.Join(", ", config.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [Fact]
    public async Task GetTopicConfigOverridesAsync_ignores_config_inherited_from_dynamic_broker_settings()
    {
        using var session = Session();
        var svc = new TopicService();

        var adminCfg = new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        using (var admin = new AdminClientBuilder(adminCfg).Build())
        {
            await admin.IncrementalAlterConfigsAsync(new Dictionary<ConfigResource, List<ConfigEntry>>
            {
                [new ConfigResource { Type = ResourceType.Broker, Name = "1" }] = new()
                {
                    new ConfigEntry { Name = "log.retention.ms", Value = "111111111",
                                      IncrementalOperation = AlterConfigOpType.Set },
                    new ConfigEntry { Name = "log.cleanup.policy", Value = "compact",
                                      IncrementalOperation = AlterConfigOpType.Set }
                }
            });
        }

        await svc.CreateTopicAsync(session, "inherits-topic", 1, 1);
        var config = await svc.GetTopicConfigOverridesAsync(session, "inherits-topic");

        Assert.True(config.Count == 0,
            $"Expected no overrides, got {config.Count}: {string.Join(", ", config.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Kafka.Tests --filter "FullyQualifiedName~GetTopicConfigOverridesAsync"`
Expected: FAIL com erro de compilação — `GetTopicConfigOverridesAsync` não existe.

- [ ] **Step 3: Renomear e corrigir o filtro**

Em `Skat.KawkaProject.Core/Interfaces/ITopicService.cs`, substituir a linha 12:

```csharp
    /// <summary>
    /// Returns ONLY the config entries explicitly overridden at topic level.
    /// Broker-level (<c>server.properties</c>) and Kafka built-in defaults are excluded,
    /// so an empty result means "no overrides", not "no configuration".
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetTopicConfigOverridesAsync(IKafkaSession session, string topicName);
```

Em `Skat.KawkaProject.Kafka/TopicService.cs`, substituir o método inteiro (linhas 76-86):

```csharp
    public async Task<IReadOnlyDictionary<string, string>> GetTopicConfigOverridesAsync(
        IKafkaSession session, string topicName)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        var results = await admin.DescribeConfigsAsync(new[]
        {
            new ConfigResource { Type = ResourceType.Topic, Name = topicName }
        });

        // IsDefault is only true for Kafka's built-in default, so a value inherited from
        // the broker's server.properties (source StaticBrokerConfig) would pass an
        // !IsDefault filter and get written back as a permanent topic-level override.
        // Only DynamicTopicConfig means "someone set this on this topic".
        return results[0].Entries.Values
            .Where(e => e.Source == ConfigSource.DynamicTopicConfig)
            .ToDictionary(e => e.Name, e => e.Value);
    }
```

Em `Skat.KawkaProject.Kafka/TopicService.cs`, dentro de `RecreateTopicWithFewerPartitionsAsync`, trocar a chamada:

```csharp
        var config = await GetTopicConfigOverridesAsync(session, topicName);
```

- [ ] **Step 4: Rodar os testes para confirmar que passam**

Run: `dotnet build && dotnet test Skat.KawkaProject.Kafka.Tests --filter "FullyQualifiedName~Config"`
Expected: PASS. Se `dotnet build` acusar `GetTopicConfigAsync` em outro arquivo, renomear lá também — o compilador aponta cada uso.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Core/Interfaces/ITopicService.cs Skat.KawkaProject.Kafka/TopicService.cs Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs
git commit -m "fix(kafka): preserve only topic-level config overrides on recreate"
```

---

### Task 3: Loop de espera com sinal positivo, durações nomeadas e `ConfigureAwait`

Três defeitos no mesmo método: `meta.Topics` vazio satisfaz `!Any(...)` igual à deleção real; as quatro durações interagem de um jeito que ninguém prevê lendo o código; e nenhuma continuação usa `ConfigureAwait(false)` numa biblioteca.

**Files:**
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs` (método `WaitForTopicDeletionAsync`, linhas 109-129, e todos os `await` do arquivo)

**Interfaces:**
- Consumes: nada.
- Produces: constantes `private static readonly TimeSpan` no escopo da classe: `DeletionPropagationGrace`, `DeletionPollInterval`, `MetadataQueryTimeout`, `DeletionTimeout`. Task 4 reusa `MetadataQueryTimeout`.

- [ ] **Step 1: Substituir o método de espera**

Em `Skat.KawkaProject.Kafka/TopicService.cs`, substituir `WaitForTopicDeletionAsync` (linhas 109-129) por:

```csharp
    private static readonly TimeSpan DeletionPropagationGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DeletionPollInterval     = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MetadataQueryTimeout     = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DeletionTimeout          = TimeSpan.FromSeconds(30);

    private static async Task WaitForTopicDeletionAsync(IAdminClient admin, string topicName)
    {
        // DeleteTopicsAsync returns as soon as the controller ACCEPTS the request; brokers
        // learn about it asynchronously via UpdateMetadata. Polling immediately would just
        // read pre-deletion metadata, so give propagation a head start before the first poll.
        await Task.Delay(DeletionPropagationGrace).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + DeletionTimeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);

                // A broker that just restarted answers with an empty/partial topic list before
                // the controller re-populates it. Absence of the topic in a degenerate response
                // is NOT evidence of deletion — requiring a non-empty list keeps us from
                // recreating while the deletion is still in flight (which fails with
                // "topic is marked for deletion" and leaves the topic destroyed).
                if (meta.Topics.Count > 0 && meta.Topics.All(t => t.Topic != topicName)) return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(DeletionPollInterval).ConfigureAwait(false);
        }

        // NOTE on the budget: a slow broker can burn the full MetadataQueryTimeout per attempt,
        // so the worst case is ~3 polls inside DeletionTimeout, not the ~60 that
        // "poll every 500ms for 30s" suggests at a glance.
        var detail = lastException != null
            ? $" Last metadata error: {lastException.Message}"
            : " Metadata queries succeeded but the topic was still listed.";

        throw new TimeoutException(
            $"Timed out after {DeletionTimeout.TotalSeconds:0}s waiting for topic '{topicName}' to disappear " +
            $"from cluster metadata.{detail}",
            lastException);
    }
```

- [ ] **Step 2: Adicionar `ConfigureAwait(false)` em todo o restante do arquivo**

Em `Skat.KawkaProject.Kafka/TopicService.cs`, acrescentar `.ConfigureAwait(false)` a cada `await` restante. As linhas afetadas e sua forma final:

```csharp
        // em ListTopicsAsync
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);

        // em GetTopicDetailAsync
        var meta = await Task.Run(() => admin.GetMetadata(topicName, MetadataQueryTimeout)).ConfigureAwait(false);

        // em CreateTopicAsync
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification { Name = name, NumPartitions = partitionCount, ReplicationFactor = replicationFactor }
        }).ConfigureAwait(false);

        // em DeleteTopicAsync
        await admin.DeleteTopicsAsync(new[] { topicName }).ConfigureAwait(false);

        // em ExpandPartitionsAsync
        await admin.CreatePartitionsAsync(new[]
        {
            new PartitionsSpecification { Topic = topicName, IncreaseTo = newPartitionCount }
        }).ConfigureAwait(false);

        // em GetTopicConfigOverridesAsync
        var results = await admin.DescribeConfigsAsync(new[]
        {
            new ConfigResource { Type = ResourceType.Topic, Name = topicName }
        }).ConfigureAwait(false);

        // em GetPartitionCountAsync
        var meta = await Task.Run(() => admin.GetMetadata(topicName, MetadataQueryTimeout)).ConfigureAwait(false);

        // em RecreateTopicWithFewerPartitionsAsync
        var currentCount = await GetPartitionCountAsync(admin, topicName).ConfigureAwait(false);
        var config = await GetTopicConfigOverridesAsync(session, topicName).ConfigureAwait(false);
        await admin.DeleteTopicsAsync(new[] { topicName }).ConfigureAwait(false);
        await WaitForTopicDeletionAsync(admin, topicName).ConfigureAwait(false);
        await admin.CreateTopicsAsync(new[] { /* ...spec inalterada... */ }).ConfigureAwait(false);
```

Também substituir os literais `TimeSpan.FromSeconds(10)` restantes por `MetadataQueryTimeout`, conforme já mostrado acima.

- [ ] **Step 3: Mover a enumeração de partições para fora da UI thread**

Em `Skat.KawkaProject.Kafka/TopicService.cs`, dentro de `GetTopicDetailAsync`, substituir o bloco `var partitions = topicMeta.Partitions.Select(...)` (linhas 40-45) por:

```csharp
        // QueryWatermarkOffsets is a BLOCKING call with a 5s timeout, once per partition.
        // Without this Task.Run the whole loop runs on whatever thread the await resumed on
        // (the Avalonia UI thread), freezing the window for up to 5s x partitionCount when a
        // broker is unreachable. Task.Run keeps it on the thread pool.
        var partitions = await Task.Run(() => topicMeta.Partitions.Select(p =>
        {
            var tp = new TopicPartition(topicName, new Partition(p.PartitionId));
            var wm = consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5));
            return new PartitionInfo(p.PartitionId, p.Leader, wm.Low.Value, wm.High.Value);
        }).ToList()).ConfigureAwait(false);
```

- [ ] **Step 4: Eliminar a auto-criação em `GetTopicDetailAsync`**

Achado da revisão de QA da Task 1 (O-4), medido contra broker real: `admin.GetMetadata(topicName, ...)` — o overload de **um tópico nomeado** — auto-cria o tópico quando `auto.create.topics.enable=true`, que é o default do broker e o do container de teste.

Cenário concreto: o usuário clica num tópico da lista que outra pessoa acabou de deletar. Em vez de erro, o app **recria** o tópico, vazio, com o `num.partitions` do broker — só por ter aberto a tela de detalhe.

Em `Skat.KawkaProject.Kafka/TopicService.cs`, dentro de `GetTopicDetailAsync`, substituir as duas linhas de leitura de metadata:

```csharp
        // Full-cluster metadata: the GetMetadata(topicName, ...) overload auto-creates the topic
        // when auto.create.topics.enable is on, which would turn "open the detail view of a topic
        // someone just deleted" into "silently recreate it".
        var meta = await Task.Run(() => admin.GetMetadata(MetadataQueryTimeout)).ConfigureAwait(false);
        var topicMeta = meta.Topics.FirstOrDefault(t => t.Topic == topicName)
            ?? throw new InvalidOperationException($"Topic '{topicName}' was not found on the cluster.");
```

Teste correspondente, em `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`:

```csharp
    [Fact]
    public async Task GetTopicDetailAsync_does_not_auto_create_an_unknown_topic()
    {
        using var session = Session();
        var svc = new TopicService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GetTopicDetailAsync(session, "detail-never-existed"));

        var topics = await svc.ListTopicsAsync(session);
        Assert.DoesNotContain(topics, t => t.Name == "detail-never-existed");
    }
```

- [ ] **Step 5: Rodar a suíte completa**

Run: `dotnet build && dotnet test`
Expected: PASS, sem regressões. O build deve terminar com `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add Skat.KawkaProject.Kafka/TopicService.cs Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs
git commit -m "fix(kafka): require positive deletion signal, name wait timings, add ConfigureAwait"
```

---

### Task 4: Exceção tipada com etapa e config preservada + retry no create

Se qualquer coisa entre o delete e o create falha, `config` é coletado pelo GC e a única cópia dos overrides do tópico some junto com o tópico. O formulário "New Topic" da UI não aceita configs, então o usuário não consegue restaurar nem manualmente.

**Files:**
- Create: `Skat.KawkaProject.Core/Exceptions/TopicRecreateFailedException.cs`
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs` (`RecreateTopicWithFewerPartitionsAsync`)
- Test: `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`

**Interfaces:**
- Consumes: `MetadataQueryTimeout` (Task 3), `GetTopicConfigOverridesAsync` (Task 2).
- Produces: `TopicRecreateFailedException` com `Stage` (enum `TopicRecreateStage`), `TopicMayBeDeleted` (bool), `PreservedConfig` (`IReadOnlyDictionary<string, string>`). Task 5 lê os três.

- [ ] **Step 1: Criar a exceção**

Criar `Skat.KawkaProject.Core/Exceptions/TopicRecreateFailedException.cs`:

```csharp
namespace Skat.KawkaProject.Core.Exceptions;

/// <summary>Which step of the delete-and-recreate sequence failed.</summary>
public enum TopicRecreateStage
{
    /// <summary>Reading the topic's config overrides. Nothing destructive has happened yet.</summary>
    ReadingConfig,

    /// <summary>Issuing the delete. The request may or may not have reached the controller.</summary>
    Deleting,

    /// <summary>Waiting for the deletion to propagate. The delete WAS accepted.</summary>
    WaitingForDeletion,

    /// <summary>Recreating the topic. The old topic and all its messages are gone.</summary>
    Creating
}

/// <summary>
/// Thrown when <c>RecreateTopicWithFewerPartitionsAsync</c> fails. Carries the stage that
/// failed and the config overrides read before the delete, so the caller can tell the user
/// whether their data is at risk and what the topic was configured with.
/// </summary>
public class TopicRecreateFailedException : Exception
{
    public TopicRecreateStage Stage { get; }

    /// <summary>
    /// True once the delete has been issued. Kafka deletion is asynchronous and irrevocable,
    /// so this means "the topic may already be gone or about to be" — NOT "confirmed deleted".
    /// Any failure with this flag set must be reported to the user as potential data loss.
    /// </summary>
    public bool TopicMayBeDeleted => Stage != TopicRecreateStage.ReadingConfig;

    /// <summary>Topic-level config overrides captured before the delete. Empty if the read itself failed.</summary>
    public IReadOnlyDictionary<string, string> PreservedConfig { get; }

    public TopicRecreateFailedException(
        TopicRecreateStage stage,
        IReadOnlyDictionary<string, string> preservedConfig,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Stage = stage;
        PreservedConfig = preservedConfig;
    }
}
```

- [ ] **Step 2: Escrever o teste que falha**

Adicionar a `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`:

```csharp
    [Fact]
    public async Task RecreateTopicWithFewerPartitionsAsync_reports_stage_and_preserves_config_on_create_failure()
    {
        var svc = new TopicService();
        using var session = Session();

        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        }).Build();

        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = "fail-topic",
                NumPartitions = 4,
                ReplicationFactor = 1,
                Configs = new Dictionary<string, string> { ["retention.ms"] = "604800000" }
            }
        });

        // Replication factor 99 on a single-broker container makes CreateTopicsAsync fail
        // AFTER the delete has already happened - the exact failure mode we must survive.
        var ex = await Assert.ThrowsAsync<TopicRecreateFailedException>(
            () => svc.RecreateTopicWithFewerPartitionsAsync(session, "fail-topic", 2, 99));

        Assert.Equal(TopicRecreateStage.Creating, ex.Stage);
        Assert.True(ex.TopicMayBeDeleted);
        Assert.Equal("604800000", ex.PreservedConfig["retention.ms"]);
    }
```

Adicionar o `using` no topo do arquivo de teste:

```csharp
using Skat.KawkaProject.Core.Exceptions;
```

- [ ] **Step 3: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Kafka.Tests --filter "FullyQualifiedName~reports_stage_and_preserves_config"`
Expected: FAIL — hoje a exceção que sobe é a `CreateTopicsException` crua do Confluent, não `TopicRecreateFailedException`.

- [ ] **Step 4: Envolver cada etapa e adicionar retry**

Em `Skat.KawkaProject.Kafka/TopicService.cs`, adicionar no topo do arquivo:

```csharp
using Skat.KawkaProject.Core.Exceptions;
```

E substituir `RecreateTopicWithFewerPartitionsAsync` inteiro por:

```csharp
    private static readonly TimeSpan CreateRetryDelay = TimeSpan.FromSeconds(2);
    private const int CreateAttempts = 3;

    public async Task RecreateTopicWithFewerPartitionsAsync(
        IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();

        // Validate BEFORE anything destructive: once DeleteTopicsAsync is issued the
        // deletion is asynchronous and irrevocable, so an invalid argument discovered
        // afterwards costs the user their data.
        var currentCount = await GetPartitionCountAsync(admin, topicName).ConfigureAwait(false);
        if (newPartitionCount < 1 || newPartitionCount >= currentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newPartitionCount), newPartitionCount,
                $"Must be between 1 and {currentCount - 1}: topic '{topicName}' currently has {currentCount} partitions, " +
                "and this operation only reduces the partition count.");
        }

        IReadOnlyDictionary<string, string> config = new Dictionary<string, string>();
        try
        {
            config = await GetTopicConfigOverridesAsync(session, topicName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TopicRecreateFailedException(TopicRecreateStage.ReadingConfig, config,
                $"Could not read the configuration of topic '{topicName}'; it was NOT modified.", ex);
        }

        try
        {
            await admin.DeleteTopicsAsync(new[] { topicName }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TopicRecreateFailedException(TopicRecreateStage.Deleting, config,
                $"The delete request for topic '{topicName}' failed. It may or may not have reached the controller.", ex);
        }

        try
        {
            await WaitForTopicDeletionAsync(admin, topicName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TopicRecreateFailedException(TopicRecreateStage.WaitingForDeletion, config,
                $"Deletion of topic '{topicName}' was accepted but could not be confirmed in time, " +
                "so the topic was not recreated.", ex);
        }

        // The delete already happened. This is the one call we must fight hardest to land,
        // so transient broker unavailability gets retried before we give up on the user's topic.
        Exception? lastCreateError = null;
        for (var attempt = 1; attempt <= CreateAttempts; attempt++)
        {
            try
            {
                await admin.CreateTopicsAsync(new[]
                {
                    new TopicSpecification
                    {
                        Name = topicName,
                        NumPartitions = newPartitionCount,
                        ReplicationFactor = replicationFactor,
                        Configs = new Dictionary<string, string>(config)
                    }
                }).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                lastCreateError = ex;
                if (attempt < CreateAttempts) await Task.Delay(CreateRetryDelay).ConfigureAwait(false);
            }
        }

        throw new TopicRecreateFailedException(TopicRecreateStage.Creating, config,
            $"Topic '{topicName}' was deleted but could not be recreated after {CreateAttempts} attempts.",
            lastCreateError!);
    }
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet build && dotnet test Skat.KawkaProject.Kafka.Tests`
Expected: PASS, incluindo os testes das Tasks 1 e 2.

- [ ] **Step 6: Commit**

```bash
git add Skat.KawkaProject.Core/Exceptions/TopicRecreateFailedException.cs Skat.KawkaProject.Kafka/TopicService.cs Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs
git commit -m "feat(kafka): report recreate failure stage and preserve topic config on failure"
```

---

# FASE 2 — UI honesta

### Task 5: Avisar de perda de dados em toda falha pós-delete

Hoje a VM só avisa quando `stillExists == false`. No modo de falha mais provável — timeout de propagação — `ListTopicsAsync` ainda lista o tópico, então o usuário lê "timed out" e conclui que nada aconteceu, enquanto a deleção completa segundos depois.

**Files:**
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs:141-179`
- Test: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: `TopicRecreateFailedException`, `TopicRecreateStage` (Task 4).
- Produces: `private static string BuildRecreateFailureMessage(TopicRecreateFailedException ex, string topicName)`.

- [ ] **Step 1: Escrever os testes que falham**

Adicionar a `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`, antes do `}` final:

```csharp
    private static TopicDetail FourPartitionDetail() => new(
        new TopicInfo("orders", 4, 1),
        new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0), new(2, 1, 0, 0), new(3, 1, 0, 0) });

    private static Mock<ITopicService> ServiceWithOrders()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(FourPartitionDetail());
        return svc;
    }

    [Theory]
    [InlineData(TopicRecreateStage.Deleting)]
    [InlineData(TopicRecreateStage.WaitingForDeletion)]
    [InlineData(TopicRecreateStage.Creating)]
    public async Task RecreateTopicAsync_warns_about_data_loss_for_every_post_delete_failure(TopicRecreateStage stage)
    {
        var svc = ServiceWithOrders();
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1))
           .ThrowsAsync(new TopicRecreateFailedException(
               stage,
               new Dictionary<string, string> { ["retention.ms"] = "604800000" },
               "boom", new InvalidOperationException("broker unreachable")));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        Assert.Contains("DATA LOSS RISK", vm.ErrorMessage);
        Assert.Contains("retention.ms=604800000", vm.ErrorMessage);
    }

    [Fact]
    public async Task RecreateTopicAsync_does_not_warn_when_failure_predates_the_delete()
    {
        var svc = ServiceWithOrders();
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1))
           .ThrowsAsync(new TopicRecreateFailedException(
               TopicRecreateStage.ReadingConfig,
               new Dictionary<string, string>(),
               "could not read config", new InvalidOperationException("nope")));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        Assert.DoesNotContain("DATA LOSS RISK", vm.ErrorMessage);
        Assert.Contains("NOT modified", vm.ErrorMessage);
    }
```

Adicionar no topo do arquivo de teste:

```csharp
using Skat.KawkaProject.Core.Exceptions;
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test Skat.KawkaProject.Features.Tests --filter "FullyQualifiedName~warns_about_data_loss|FullyQualifiedName~predates_the_delete"`
Expected: FAIL — hoje a mensagem para `WaitingForDeletion` é só `ex.Message`, sem aviso de perda.

- [ ] **Step 3: Substituir o bloco catch**

Em `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, adicionar no topo:

```csharp
using Skat.KawkaProject.Core.Exceptions;
```

E substituir o `catch (Exception ex)` de `RecreateTopicAsync` (linhas 162-177) por:

```csharp
        catch (TopicRecreateFailedException ex)
        {
            ErrorMessage = BuildRecreateFailureMessage(ex, topicName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
```

E adicionar o método auxiliar logo depois de `RecreateTopicAsync`:

```csharp
    private static string BuildRecreateFailureMessage(TopicRecreateFailedException ex, string topicName)
    {
        var cause = ex.InnerException?.Message ?? ex.Message;

        if (!ex.TopicMayBeDeleted)
            return $"Could not recreate '{topicName}': {cause}. The topic was NOT modified.";

        // Kafka deletion is asynchronous and irrevocable once issued: the topic still being
        // listed right now does not mean it is safe, it means "not yet". Every failure at or
        // after the delete stage must be reported as potential data loss.
        var configNote = ex.PreservedConfig.Count > 0
            ? " Its config overrides were: " +
              string.Join(", ", ex.PreservedConfig.Select(kv => $"{kv.Key}={kv.Value}"))
            : " It had no config overrides.";

        return $"DATA LOSS RISK: deletion of '{topicName}' was already issued and cannot be undone, " +
               $"but the topic could not be recreated: {cause}. Verify the topic on your cluster and " +
               $"recreate it manually if needed.{configNote}";
    }
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test Skat.KawkaProject.Features.Tests`
Expected: PASS. Todos os testes pré-existentes continuam verdes.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "fix(topics): warn about data loss on every post-delete recreate failure"
```

---

### Task 6: Sincronizar lista e seleção após a operação

Dois bugs de estado: em caso de falha a lista continua mostrando o tópico morto; em caso de sucesso `ApplyFilter()` limpa a `ObservableCollection`, o ListBox escreve `null` de volta em `SelectedTopic`, e o botão 🗑 Delete passa a disparar com nome nulo.

**Files:**
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Modify: `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml:199`
- Test: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: `BuildRecreateFailureMessage` (Task 5).
- Produces: `private void ReselectTopicByName(string topicName)`.

- [ ] **Step 1: Escrever os testes que falham**

Adicionar a `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`:

```csharp
    [Fact]
    public async Task RecreateTopicAsync_reselects_the_refreshed_topic_on_success()
    {
        var svc = new Mock<ITopicService>();
        svc.SetupSequence(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) })
           .ReturnsAsync(new[] { new TopicInfo("orders", 2, 1) });
        svc.SetupSequence(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(FourPartitionDetail())
           .ReturnsAsync(new TopicDetail(new TopicInfo("orders", 2, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0), new(1, 1, 0, 0) }));
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1))
           .Returns(Task.CompletedTask);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        Assert.NotNull(vm.SelectedTopic);
        Assert.Equal("orders", vm.SelectedTopic!.Name);
        Assert.Equal(2, vm.SelectedTopic.PartitionCount);
    }

    [Fact]
    public async Task RecreateTopicAsync_drops_the_topic_from_the_list_when_it_is_gone()
    {
        var svc = new Mock<ITopicService>();
        svc.SetupSequence(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("orders", 4, 1) })
           .ReturnsAsync(Array.Empty<TopicInfo>());
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "orders"))
           .ReturnsAsync(FourPartitionDetail());
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1))
           .ThrowsAsync(new TopicRecreateFailedException(
               TopicRecreateStage.Creating, new Dictionary<string, string>(),
               "gone", new InvalidOperationException("create failed")));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        await vm.RecreateTopicAsync();

        Assert.Empty(vm.Topics);
        Assert.Null(vm.SelectedTopicDetail);
    }
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test Skat.KawkaProject.Features.Tests --filter "FullyQualifiedName~reselects_the_refreshed|FullyQualifiedName~drops_the_topic"`
Expected: FAIL — `SelectedTopic` é null no primeiro; a lista ainda tem 1 item no segundo.

- [ ] **Step 3: Implementar a sincronização**

Em `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, substituir o corpo do `try`/`catch` de `RecreateTopicAsync` por:

```csharp
        try
        {
            await _topicService.RecreateTopicWithFewerPartitionsAsync(_session, topicName, requested, replicationFactor);
            IsRecreatingTopic = false;
            await LoadTopicsAsync();
            await LoadDetailAsync(topicName);
            ReselectTopicByName(topicName);
        }
        catch (TopicRecreateFailedException ex)
        {
            ErrorMessage = BuildRecreateFailureMessage(ex, topicName);
            if (ex.TopicMayBeDeleted) await ResyncAfterPossibleDeletionAsync(topicName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
```

E adicionar os dois helpers logo após `BuildRecreateFailureMessage`:

```csharp
    /// <summary>
    /// Re-points SelectedTopic at the refreshed TopicInfo instance. ApplyFilter clears the
    /// ObservableCollection, which makes the ListBox write null back through the two-way
    /// binding; the re-added item is a different record value (the partition count changed),
    /// so it is never auto-reselected. Without this the detail panel stays open while
    /// SelectedTopic is null, and the Delete button fires with a null topic name.
    /// </summary>
    private void ReselectTopicByName(string topicName)
    {
        var refreshed = Topics.FirstOrDefault(t => t.Name == topicName);
        if (refreshed == null) return;
        _selectedTopic = refreshed;
        this.RaisePropertyChanged(nameof(SelectedTopic));
    }

    /// <summary>
    /// After a failure that may have deleted the topic, refresh the list so the UI stops
    /// offering actions on something that no longer exists.
    /// </summary>
    private async Task ResyncAfterPossibleDeletionAsync(string topicName)
    {
        try
        {
            _allTopics = (await _topicService.ListTopicsAsync(_session)).ToList();
            ApplyFilter();
            if (_allTopics.All(t => t.Name != topicName))
            {
                _selectedTopic = null;
                this.RaisePropertyChanged(nameof(SelectedTopic));
                SelectedTopicDetail = null;
            }
            else
            {
                ReselectTopicByName(topicName);
            }
        }
        catch
        {
            // The refresh itself failed (likely the same outage that broke the recreate).
            // Keep the data-loss message already in ErrorMessage - it is the important one.
        }
    }
```

- [ ] **Step 4: Apontar o botão Delete para uma fonte não-nula**

Em `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, na linha 199, substituir:

```xml
                                    CommandParameter="{Binding SelectedTopic.Name}"
```

por:

```xml
                                    CommandParameter="{Binding SelectedTopicDetail.Topic.Name}"
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet build && dotnet test Skat.KawkaProject.Features.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "fix(topics): resync list and selection after recreate success or failure"
```

---

### Task 7: Travar comandos durante operações

`IsBusy` hoje dirige exatamente uma coisa: uma barra de progresso de 3px. Nenhum comando tem `canExecute`. Durante os até 30s de espera do recreate, o usuário pode clicar 🗑 Delete no mesmo tópico e destruir a versão recém-criada — enquanto o recreate reporta sucesso.

**Files:**
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs:75`
- Modify: `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`
- Test: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `public bool IsNotBusy => !_isBusy;` — usado pelos bindings de `IsEnabled`.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
    [Fact]
    public async Task IsNotBusy_tracks_IsBusy_and_notifies()
    {
        var svc = ServiceWithOrders();
        var tcs = new TaskCompletionSource();
        svc.Setup(s => s.RecreateTopicWithFewerPartitionsAsync(It.IsAny<IKafkaSession>(), "orders", 2, 1))
           .Returns(tcs.Task);

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = 2;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var running = vm.RecreateTopicAsync();
        Assert.False(vm.IsNotBusy);

        tcs.SetResult();
        await running;

        Assert.True(vm.IsNotBusy);
        Assert.Contains(nameof(TopicsViewModel.IsNotBusy), raised);
    }
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Features.Tests --filter "FullyQualifiedName~IsNotBusy_tracks"`
Expected: FAIL com erro de compilação — `IsNotBusy` não existe.

- [ ] **Step 3: Adicionar a propriedade**

Em `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, substituir a linha 75:

```csharp
    public bool IsBusy
    {
        get => _isBusy;
        private set { this.RaiseAndSetIfChanged(ref _isBusy, value); this.RaisePropertyChanged(nameof(IsNotBusy)); }
    }

    /// <summary>Bound to IsEnabled on every mutating control: a long operation (recreate can
    /// wait up to 30s) must not leave other destructive buttons live.</summary>
    public bool IsNotBusy => !_isBusy;
```

- [ ] **Step 4: Bindar `IsEnabled` nos controles mutantes**

Em `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, adicionar `IsEnabled="{Binding IsNotBusy}"` a cada um dos seguintes elementos (mantendo todos os outros atributos intactos):

- o `<Button Command="{Binding ShowExpandFormCommand}"` (bloco "Action buttons")
- o `<Button Command="{Binding ShowRecreateFormCommand}"` (bloco "Action buttons")
- o `<Button Command="{Binding DeleteTopicCommand}"` (bloco "Action buttons")
- o `<Button Command="{Binding ExpandPartitionsCommand}"` (formulário de increase)
- o `<Button Command="{Binding CreateTopicCommand}"` (formulário de criação)

No botão "Recreate topic", que já tem `IsEnabled="{Binding CanConfirmRecreate}"`, trocar por um `MultiBinding` — Avalonia não permite dois `IsEnabled`:

```xml
                                <Button Command="{Binding RecreateTopicCommand}"
                                        FontSize="11" Padding="8,4"
                                        Background="{DynamicResource DestructiveBrush}"
                                        BorderBrush="{DynamicResource StatusErrorBrush}"
                                        Foreground="{DynamicResource DestructiveTextBrush}">
                                    <Button.IsEnabled>
                                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                                            <Binding Path="CanConfirmRecreate" />
                                            <Binding Path="IsNotBusy" />
                                        </MultiBinding>
                                    </Button.IsEnabled>
                                    Recreate topic
                                </Button>
```

- [ ] **Step 5: Rodar build e testes**

Run: `dotnet build && dotnet test Skat.KawkaProject.Features.Tests`
Expected: PASS, `0 Error(s)`. Se o AXAML acusar `BoolConverters` desconhecido, confirmar que `xmlns:x` está declarado no topo do arquivo (já está) — `BoolConverters` vive em `Avalonia.Data.Converters`, resolvido pelo namespace padrão do Avalonia.

- [ ] **Step 6: Commit**

```bash
git add Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "fix(topics): disable mutating commands while an operation is in flight"
```

---

### Task 8: Contagem nulável e mensagem de erro sensata

`NumericUpDown.Value` é `decimal?`; a propriedade é `int` não-nulável. Limpar o campo faz a escrita do binding falhar em silêncio e a VM segue com o valor antigo — então o tópico é recriado com um número que o usuário nunca escolheu e que não está na tela. E um tópico de 1 partição produz "must be between 1 and 0".

**Files:**
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Test: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `RecreatePartitionCount` e `NewPartitionCount` passam de `int` para `int?`. Os testes existentes que fazem `vm.RecreatePartitionCount = 2` seguem compilando (conversão implícita).

- [ ] **Step 1: Escrever os testes que falham**

```csharp
    [Fact]
    public async Task RecreateTopicAsync_refuses_when_partition_count_is_empty()
    {
        var svc = ServiceWithOrders();
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = null;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.RecreateTopicWithFewerPartitionsAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>()), Times.Never);
        Assert.Contains("Enter the new partition count", vm.ErrorMessage);
    }

    [Fact]
    public async Task RecreateTopicAsync_explains_that_a_single_partition_topic_cannot_shrink()
    {
        var svc = new Mock<ITopicService>();
        svc.Setup(s => s.ListTopicsAsync(It.IsAny<IKafkaSession>()))
           .ReturnsAsync(new[] { new TopicInfo("solo", 1, 1) });
        svc.Setup(s => s.GetTopicDetailAsync(It.IsAny<IKafkaSession>(), "solo"))
           .ReturnsAsync(new TopicDetail(new TopicInfo("solo", 1, 1),
               new List<PartitionInfo> { new(0, 1, 0, 0) }));

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "solo";
        vm.RecreatePartitionCount = 1;

        await vm.RecreateTopicAsync();

        Assert.Contains("nothing to reduce", vm.ErrorMessage);
        Assert.DoesNotContain("between 1 and 0", vm.ErrorMessage);
    }
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test Skat.KawkaProject.Features.Tests --filter "FullyQualifiedName~partition_count_is_empty|FullyQualifiedName~single_partition_topic"`
Expected: FAIL — o primeiro não compila (`null` em `int`); o segundo produz "must be between 1 and 0".

- [ ] **Step 3: Tornar os campos nuláveis e corrigir as guardas**

Em `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`, substituir as declarações de campo (linhas 32 e 34):

```csharp
    private int? _newPartitionCount = 1;
    private int? _recreatePartitionCount = 1;
```

Substituir as propriedades (linhas 44 e 47):

```csharp
    /// <summary>Nullable because NumericUpDown.Value is decimal? — clearing the box must
    /// mean "no value", not "silently keep the previous one" on a destructive operation.</summary>
    public int? NewPartitionCount { get => _newPartitionCount; set => this.RaiseAndSetIfChanged(ref _newPartitionCount, value); }

    /// <summary>Nullable for the same reason as <see cref="NewPartitionCount"/>.</summary>
    public int? RecreatePartitionCount { get => _recreatePartitionCount; set => this.RaiseAndSetIfChanged(ref _recreatePartitionCount, value); }
```

Substituir o bloco de guarda de `RecreateTopicAsync` (linhas 143-149) por:

```csharp
        if (SelectedTopicDetail == null || !CanConfirmRecreate) return;
        var currentCount = SelectedTopicDetail.Partitions.Count;

        if (currentCount <= 1)
        {
            ErrorMessage = $"'{SelectedTopicDetail.Topic.Name}' already has a single partition — there is nothing to reduce.";
            return;
        }
        if (_recreatePartitionCount is not int requested)
        {
            ErrorMessage = "Enter the new partition count.";
            return;
        }
        if (requested < 1 || requested >= currentCount)
        {
            ErrorMessage = $"New partition count must be between 1 and {currentCount - 1} " +
                           $"(the topic currently has {currentCount}).";
            return;
        }
```

Na mesma função, trocar o uso de `_recreatePartitionCount` na chamada do serviço por `requested` (já aplicado na Task 6).

Substituir o bloco de guarda de `ExpandPartitionsAsync` (linhas 183-189) por:

```csharp
        if (SelectedTopicDetail == null) return;
        var currentCount = SelectedTopicDetail.Partitions.Count;

        if (_newPartitionCount is not int requested)
        {
            ErrorMessage = "Enter the new partition count.";
            return;
        }
        if (requested <= currentCount)
        {
            ErrorMessage = $"New partition count must be greater than the current count ({currentCount}).";
            return;
        }
```

E trocar a chamada do serviço dentro do `try` de `ExpandPartitionsAsync`:

```csharp
            await _topicService.ExpandPartitionsAsync(_session, topicName, requested);
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet build && dotnet test Skat.KawkaProject.Features.Tests`
Expected: PASS. O teste pré-existente `ExpandPartitionsAsync_calls_service_and_reloads_detail` continua verde.

- [ ] **Step 5: Commit**

```bash
git add Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "fix(topics): treat an empty partition-count box as no value, not stale state"
```

---

# FASE 3 — Contrato e avisos

### Task 9: Renomes honestos e documentação do contrato destrutivo

`ITopicService` é o arquivo que alguém lê para descobrir o que o app faz. `RecreateTopicWithFewerPartitions` lá no meio lê como reconfiguração — nada diz que apaga todas as mensagens. Toda a segurança dessa operação mora hoje num arquivo AXAML.

**Files:**
- Modify: `Skat.KawkaProject.Core/Interfaces/ITopicService.cs`
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs`
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Modify: `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`
- Modify: ambos os arquivos de teste

**Interfaces:**
- Produces: `DeleteAndRecreateTopicAsync` substitui `RecreateTopicWithFewerPartitionsAsync`; `ExpandToPartitionCount` substitui `NewPartitionCount`; parâmetro `partitionId` substitui `partition`.

- [ ] **Step 1: Renomear o método destrutivo com documentação**

Em `Skat.KawkaProject.Core/Interfaces/ITopicService.cs`, substituir a linha do recreate por:

```csharp
    /// <summary>
    /// DELETES the topic and recreates it with a smaller partition count. Kafka cannot shrink
    /// partitions in place, so this is destructive: <b>ALL MESSAGES ARE PERMANENTLY LOST</b>.
    /// <para>
    /// Carried over: topic-level config overrides. NOT carried over: messages, consumer group
    /// offsets, and ACLs. Consumer groups with committed offsets on this topic will be left
    /// pointing at offsets that no longer mean what they meant.
    /// </para>
    /// <para>
    /// Callers MUST obtain explicit user confirmation before calling this. Throws
    /// <see cref="ArgumentOutOfRangeException"/> if <paramref name="newPartitionCount"/> is not
    /// in [1, current-1], and <see cref="TopicRecreateFailedException"/> (carrying the failed
    /// stage and the preserved config) on any failure during the sequence.
    /// </para>
    /// </summary>
    Task DeleteAndRecreateTopicAsync(IKafkaSession session, string topicName, int newPartitionCount, short replicationFactor);
```

Adicionar o `using` no topo do arquivo de interface:

```csharp
using Skat.KawkaProject.Core.Exceptions;
```

- [ ] **Step 2: Propagar o rename**

Renomear em `Skat.KawkaProject.Kafka/TopicService.cs`, em `TopicsViewModel.cs` (a chamada dentro de `RecreateTopicAsync`), e em ambos os arquivos de teste. O compilador aponta cada uso:

Run: `dotnet build 2>&1 | grep -E "error CS"`
Expected: a lista exata de arquivos/linhas a ajustar; corrigir todos até o build ficar limpo.

- [ ] **Step 3: Renomear a propriedade genérica e o parâmetro**

Em `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`:

- `_newPartitionCount` → `_expandToPartitionCount`
- `NewPartitionCount` → `ExpandToPartitionCount`
- assinatura `public void ViewPartitionMessages(int partition)` → `public void ViewPartitionMessages(int partitionId)`, e o corpo passa a usar `partitionId`

Em `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, na linha do formulário de increase, substituir:

```xml
                                <NumericUpDown Value="{Binding NewPartitionCount}" Minimum="1"
```

por:

```xml
                                <NumericUpDown Value="{Binding ExpandToPartitionCount}" Minimum="1"
```

Em `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`, substituir as duas ocorrências de `vm.NewPartitionCount` por `vm.ExpandToPartitionCount`.

- [ ] **Step 4: Renomear o teste de integração enganoso**

Em `Skat.KawkaProject.Kafka.Tests/TopicServiceIntegrationTests.cs`, renomear:

```csharp
    public async Task DeleteAndRecreateTopicAsync_reduces_partitions_and_preserves_overridden_configs()
```

- [ ] **Step 5: Rodar build e suíte completa**

Run: `dotnet build && dotnet test`
Expected: PASS, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(topics): rename destructive method and document its contract"
```

---

### Task 10: Avisar sobre offsets, ACLs e replication factor não-uniforme

Três consequências reais que a UI hoje não menciona: offsets de consumer group ficam inválidos (podendo pular mensagens em silêncio), ACLs são descartadas (em cluster com `allow.everyone.if.no.acl.found=true` o tópico volta world-writable), e o RF é derivado só da partição 0.

**Files:**
- Modify: `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml` (formulário de recreate)
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs` (`GetTopicDetailAsync`, `ListTopicsAsync`)

- [ ] **Step 1: Expandir o texto de aviso**

Em `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, substituir o `TextBlock` de aviso do formulário de recreate por:

```xml
                            <TextBlock Text="This deletes and recreates the topic. All messages in this topic will be permanently lost. This cannot be undone."
                                       FontSize="10" TextWrapping="Wrap"
                                       Foreground="{DynamicResource StatusErrorBrush}" />
                            <TextBlock Text="Also NOT carried over: consumer group offsets (consumers may silently skip or replay records) and ACLs (on a cluster with allow.everyone.if.no.acl.found=true the topic comes back unrestricted). Topic-level config overrides ARE preserved."
                                       FontSize="10" TextWrapping="Wrap"
                                       Foreground="{DynamicResource TextMutedBrush}" />
```

- [ ] **Step 2: Corrigir a derivação do replication factor**

Em `Skat.KawkaProject.Kafka/TopicService.cs`, em `GetTopicDetailAsync`, substituir a construção de `info` (linhas 47-48) por:

```csharp
        // Use the MINIMUM replica count across partitions, not partition 0's. A topic with a
        // non-uniform assignment (e.g. an interrupted kafka-reassign-partitions run) would
        // otherwise report partition 0's factor, and a recreate would flatten every partition
        // to it — silently halving durability with no warning.
        var replicationFactor = (short)topicMeta.Partitions.Min(p => p.Replicas.Length);
        var info = new TopicInfo(topicMeta.Topic, partitions.Count, replicationFactor);
```

Aplicar a mesma correção em `ListTopicsAsync` (linhas 23-26):

```csharp
            .Select(t => new TopicInfo(
                t.Topic,
                t.Partitions.Count,
                (short)t.Partitions.Min(p => p.Replicas.Length)));
```

- [ ] **Step 3: Rodar build e suíte**

Run: `dotnet build && dotnet test`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml Skat.KawkaProject.Kafka/TopicService.cs
git commit -m "fix(topics): warn about offset/ACL loss and derive RF from min replica count"
```

---

# FASE 4 — Estado de UI

### Task 11: Exclusividade mútua entre os formulários inline

`ShowExpandFormCommand` e `ShowRecreateFormCommand` não se limpam. Clicar "▲ Increase" e depois "⚠ Recreate" deixa os dois formulários empilhados no mesmo painel, com dois campos "New count:" na tela — num painel cujo trabalho é tornar uma operação destrutiva inequívoca. E `IsNotExpandingPartitions`/`IsNotRecreatingTopic` prometem um comportamento de auto-ocultação que nunca foi bindado.

**Files:**
- Create: `Skat.KawkaProject.Core/Models/TopicsFormMode.cs`
- Modify: `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Test: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

**Interfaces:**
- Produces: enum `TopicsFormMode { None, Create, Expand, Recreate }`. `IsCreatingTopic`/`IsExpandingPartitions`/`IsRecreatingTopic` viram getters derivados — os bindings AXAML existentes continuam funcionando sem alteração.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
    [Fact]
    public async Task Opening_one_form_closes_the_others()
    {
        var svc = ServiceWithOrders();
        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];

        vm.ShowExpandFormCommand.Execute(null);
        Assert.True(vm.IsExpandingPartitions);

        vm.ShowRecreateFormCommand.Execute(null);
        Assert.True(vm.IsRecreatingTopic);
        Assert.False(vm.IsExpandingPartitions);

        vm.ShowCreateFormCommand.Execute(null);
        Assert.True(vm.IsCreatingTopic);
        Assert.False(vm.IsRecreatingTopic);
        Assert.False(vm.IsExpandingPartitions);
    }
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

Run: `dotnet test Skat.KawkaProject.Features.Tests --filter "FullyQualifiedName~Opening_one_form"`
Expected: FAIL — `IsExpandingPartitions` continua `true` após abrir o recreate.

- [ ] **Step 3: Criar o enum**

Criar `Skat.KawkaProject.Core/Models/TopicsFormMode.cs`:

```csharp
namespace Skat.KawkaProject.Core.Models;

/// <summary>
/// Which inline form is open in the topics detail panel. Modelled as one value rather than
/// N independent booleans because the forms share a single DockPanel — two open at once puts
/// two "New count:" inputs on screen, on the one panel whose job is to make a destructive
/// operation unambiguous.
/// </summary>
public enum TopicsFormMode
{
    None,
    Create,
    Expand,
    Recreate
}
```

- [ ] **Step 4: Substituir os três booleanos por um estado**

Em `Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`:

Remover os campos `_isCreatingTopic`, `_isExpandingPartitions`, `_isRecreatingTopic` (linhas 27, 31, 33) e adicionar no lugar:

```csharp
    private TopicsFormMode _activeForm = TopicsFormMode.None;
```

Substituir as seis propriedades booleanas (linhas 37-38, 42-43, 45-46) por:

```csharp
    public TopicsFormMode ActiveForm
    {
        get => _activeForm;
        private set
        {
            this.RaiseAndSetIfChanged(ref _activeForm, value);
            this.RaisePropertyChanged(nameof(IsCreatingTopic));
            this.RaisePropertyChanged(nameof(IsNotCreatingTopic));
            this.RaisePropertyChanged(nameof(IsExpandingPartitions));
            this.RaisePropertyChanged(nameof(IsNotExpandingPartitions));
            this.RaisePropertyChanged(nameof(IsRecreatingTopic));
            this.RaisePropertyChanged(nameof(IsNotRecreatingTopic));
        }
    }

    public bool IsCreatingTopic        => _activeForm == TopicsFormMode.Create;
    public bool IsNotCreatingTopic     => _activeForm != TopicsFormMode.Create;
    public bool IsExpandingPartitions  => _activeForm == TopicsFormMode.Expand;
    public bool IsNotExpandingPartitions => _activeForm != TopicsFormMode.Expand;
    public bool IsRecreatingTopic      => _activeForm == TopicsFormMode.Recreate;
    public bool IsNotRecreatingTopic   => _activeForm != TopicsFormMode.Recreate;
```

Substituir as atribuições nos comandos do construtor (linhas 117-136):

```csharp
        ShowCreateFormCommand = ReactiveCommand.Create(() =>
        {
            ActiveForm = TopicsFormMode.Create;
            NewTopicName = ""; NewTopicPartitions = 1; NewTopicReplicationFactor = 1;
        });
        CancelCreateCommand = ReactiveCommand.Create(() => ActiveForm = TopicsFormMode.None);
        CreateTopicCommand = ReactiveCommand.CreateFromTask(CreateTopicAsync);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);
        ViewPartitionMessagesCommand = ReactiveCommand.Create<int>(ViewPartitionMessages);
        ShowExpandFormCommand = ReactiveCommand.Create(() =>
        {
            ActiveForm = TopicsFormMode.Expand;
            ExpandToPartitionCount = (SelectedTopicDetail?.Partitions.Count ?? 0) + 1;
        });
        CancelExpandCommand = ReactiveCommand.Create(() => ActiveForm = TopicsFormMode.None);
        ExpandPartitionsCommand = ReactiveCommand.CreateFromTask(ExpandPartitionsAsync);
        ShowRecreateFormCommand = ReactiveCommand.Create(() =>
        {
            ActiveForm = TopicsFormMode.Recreate;
            RecreateConfirmName = "";
            RecreatePartitionCount = Math.Max(1, (SelectedTopicDetail?.Partitions.Count ?? 1) - 1);
        });
        CancelRecreateCommand = ReactiveCommand.Create(() => ActiveForm = TopicsFormMode.None);
        RecreateTopicCommand = ReactiveCommand.CreateFromTask(RecreateTopicAsync);
```

E substituir as três atribuições nos métodos async — `IsRecreatingTopic = false` (em `RecreateTopicAsync`), `IsExpandingPartitions = false` (em `ExpandPartitionsAsync`) e `IsCreatingTopic = false` (em `CreateTopicAsync`) — por:

```csharp
            ActiveForm = TopicsFormMode.None;
```

Adicionar o `using` se ainda não presente:

```csharp
using Skat.KawkaProject.Core.Models;
```

- [ ] **Step 5: Bindar `IsNotExpandingPartitions`/`IsNotRecreatingTopic` (cumprir a promessa)**

Em `Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`, no bloco "Action buttons", adicionar aos botões:

- `<Button Command="{Binding ShowExpandFormCommand}"` → adicionar `IsVisible="{Binding IsNotExpandingPartitions}"`
- `<Button Command="{Binding ShowRecreateFormCommand}"` → adicionar `IsVisible="{Binding IsNotRecreatingTopic}"`

- [ ] **Step 6: Rodar build e suíte**

Run: `dotnet build && dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(topics): model inline form visibility as one exclusive state"
```

---

# FASE 5 — Testes

### Task 12: Fechar as lacunas de cobertura e os nomes enganosos

`RecreateTopicAsync_rejects_count_outside_valid_range` só exercita `4` contra um tópico de 4 partições — o limite inferior nunca é tocado, então trocar `< 1` por `< 0` deixa a suíte verde. E cinco testes dependem, sem dizer, de os mocks completarem sincronamente.

**Files:**
- Modify: `Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs`

- [ ] **Step 1: Substituir o teste de faixa por um `Theory` que cobre as duas pontas**

Substituir o teste `RecreateTopicAsync_rejects_count_outside_valid_range` inteiro por:

```csharp
    [Theory]
    [InlineData(0)]   // below the minimum
    [InlineData(-1)]  // negative
    [InlineData(4)]   // equal to current — not fewer
    [InlineData(5)]   // above current
    public async Task RecreateTopicAsync_rejects_partition_count_outside_1_to_current_minus_1(int requested)
    {
        var svc = ServiceWithOrders();

        var vm = new TopicsViewModel(FakeScreen(), FakeSession(), svc.Object, NoOpNavigate);
        await vm.LoadTopicsAsync();
        vm.SelectedTopic = vm.Topics[0];
        vm.RecreateConfirmName = "orders";
        vm.RecreatePartitionCount = requested;

        await vm.RecreateTopicAsync();

        svc.Verify(s => s.DeleteAndRecreateTopicAsync(
            It.IsAny<IKafkaSession>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>()), Times.Never);
        Assert.NotNull(vm.ErrorMessage);
    }
```

- [ ] **Step 2: Documentar a dependência do mock síncrono**

Adicionar o comentário logo acima do helper `ServiceWithOrders` (criado na Task 5):

```csharp
    // NOTE for anyone adding a delayed mock here: assigning vm.SelectedTopic kicks off a
    // fire-and-forget LoadDetailAsync (TopicsViewModel.SelectedTopic setter). These tests rely
    // on SelectedTopicDetail being populated by the next line, which only holds because Moq's
    // ReturnsAsync hands back an already-completed task and the continuation runs inline.
    // Give GetTopicDetailAsync a real delay and every test below starts failing with a null
    // SelectedTopicDetail, for reasons invisible in the test body.
```

- [ ] **Step 3: Rodar a suíte completa**

Run: `dotnet test`
Expected: PASS. Contagem de testes deve subir (o `Theory` novo contribui 4 casos).

- [ ] **Step 4: Commit**

```bash
git add Skat.KawkaProject.Features.Tests/TopicsViewModelTests.cs
git commit -m "test(topics): cover the lower validation bound and document mock timing dependency"
```

---

# FASE 6 — Refactors arquiteturais (opcional, avaliar antes de executar)

> Estas duas tasks são as recomendações da 🌸 Blossom que mudam estrutura, não comportamento. Todas as correções de segurança já estão feitas nas Fases 1-5. Execute-as apenas se decidir que o padrão vai ser copiado para novas operações — se `TopicService` for permanecer com uma única saga, o custo/benefício é discutível.

### Task 13: Extrair a saga para um tipo próprio

`TopicService` é um adapter fino (wrappers de 3-6 linhas sobre uma chamada de `AdminClient`). A sequência delete→wait→create é uma saga com meio-do-caminho irreversível e não pertence a ele. Extrair também torna a política de polling injetável e permite `CancellationToken`.

**Files:**
- Create: `Skat.KawkaProject.Kafka/TopicRecreateOperation.cs`
- Modify: `Skat.KawkaProject.Kafka/TopicService.cs`

- [ ] **Step 1: Adicionar a sobrecarga de `CreateTopicAsync` com configs**

Em `Skat.KawkaProject.Core/Interfaces/ITopicService.cs`, adicionar:

```csharp
    /// <summary>Creates a topic, applying the given topic-level config overrides.</summary>
    Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor,
        IReadOnlyDictionary<string, string> configs);
```

Em `Skat.KawkaProject.Kafka/TopicService.cs`, implementar delegando a existente:

```csharp
    public Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount, short replicationFactor)
        => CreateTopicAsync(session, name, partitionCount, replicationFactor, new Dictionary<string, string>());

    public async Task CreateTopicAsync(IKafkaSession session, string name, int partitionCount,
        short replicationFactor, IReadOnlyDictionary<string, string> configs)
    {
        using var admin = new AdminClientBuilder(AdminConfig(session)).Build();
        await admin.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = name,
                NumPartitions = partitionCount,
                ReplicationFactor = replicationFactor,
                Configs = new Dictionary<string, string>(configs)
            }
        }).ConfigureAwait(false);
    }
```

- [ ] **Step 2: Mover a saga**

Criar `Skat.KawkaProject.Kafka/TopicRecreateOperation.cs` contendo a lógica hoje em `DeleteAndRecreateTopicAsync` (validação, leitura de config, delete, espera, retry de create), aceitando `ITopicService` no construtor e um `CancellationToken` opcional em cada método. `TopicService.DeleteAndRecreateTopicAsync` passa a delegar a ele.

Este passo é uma movimentação mecânica do código já testado — a suíte das Fases 1-5 é a rede de segurança.

- [ ] **Step 3: Rodar a suíte completa sem alterar nenhum teste**

Run: `dotnet test`
Expected: PASS, sem edições nos testes. Se algum teste precisar mudar, a extração alterou comportamento — reverter e refazer.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(kafka): extract delete-and-recreate saga out of the thin adapter"
```

---

### Task 14: Factory de `MessagesViewModel` e o null-check deferido

`ConnectionNodeViewModel` executa hoje um ritual de cinco passos sobre um tipo de outro módulo, em dois lugares diferentes. E o segundo `if (_session == null)` — que parece copy-paste redundante e é a coisa mais fácil do arquivo de "limpar" — na verdade guarda um momento completamente diferente no tempo.

**Files:**
- Modify: `Skat.KawkaProject.Features.Messages/ViewModels/MessagesViewModel.cs`
- Modify: `Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs:89-115`

- [ ] **Step 1: Adicionar a factory ao próprio `MessagesViewModel`**

```csharp
    /// <summary>
    /// Builds a MessagesViewModel already pointed at one partition and starts the fetch.
    /// Owning this here keeps the initialization protocol (TopicName + Partition + Mode + fetch)
    /// in one place instead of duplicated at every call site in the composition root.
    /// </summary>
    public static MessagesViewModel ForPartition(
        IScreen shell, IKafkaSession session, IMessageService messageService, ITopicService topicService,
        string topicName, int partitionId)
    {
        var vm = new MessagesViewModel(shell, session, messageService, topicService)
        {
            TopicName = topicName,
            Partition = partitionId,
            Mode = MessageMode.Offset,
        };
        _ = vm.FetchMessagesAsync();
        return vm;
    }
```

- [ ] **Step 2: Reduzir o callback e comentar o guard deferido**

Em `Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`, substituir o corpo do `NavigateToTopicsCommand` por:

```csharp
        NavigateToTopicsCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Topics.ViewModels.TopicsViewModel(
                    shell, _session, topicService, ShowPartitionMessages));
        });

        void ShowPartitionMessages(string topicName, int partitionId)
        {
            // Deferred guard, NOT a copy of the one above: this runs when the user clicks a
            // partition's eye icon, which may be minutes after opening the Topics screen — long
            // enough for DisconnectCommand to have nulled _session out from under this lambda.
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                Skat.KawkaProject.Features.Messages.ViewModels.MessagesViewModel.ForPartition(
                    shell, _session, messageService, topicService, topicName, partitionId));
        }
```

- [ ] **Step 3: Rodar build e suíte**

Run: `dotnet build && dotnet test`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(ui): let MessagesViewModel own its partition initialization"
```

---

## Verificação final

Após a Task 12 (ou 14, se as opcionais forem executadas):

- [ ] `dotnet build` → `0 Error(s)`
- [ ] `dotnet test` → todos verdes, com Docker rodando para os testes de integração
- [ ] Rodar o app e confirmar manualmente: abrir "▲ Increase" e depois "⚠ Recreate" mostra **um** formulário; recriar um tópico de 1 partição dá a mensagem "nothing to reduce"; limpar o campo de contagem e clicar "Recreate topic" dá "Enter the new partition count"; durante uma operação os botões destrutivos ficam desabilitados.

## Revisão final do projeto

Com **todas** as tasks concluídas (incluindo as opcionais 13 e 14, se executadas), rodar `/powerpuff-review` **sobre o projeto todo** — não apenas sobre o diff da última task.

O objetivo é diferente do gate por task: o `qa-tester` valida cada entrega isoladamente, enquanto esta revisão procura o que só aparece quando tudo está junto — regressões introduzidas por uma task tardia numa correção anterior, inconsistências entre as fases, e acoplamento que nenhuma task viu sozinha. Tratar os achados como entrada para um novo ciclo de plano, não como algo a corrigir às pressas no fim.
