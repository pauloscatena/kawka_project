# Headless Text Interface (TUI) — Design

## Goal

Dar ao Kawka uma interface de texto para uso em Linux sem modo gráfico —
tipicamente por SSH num servidor — cobrindo o mesmo conjunto de operações do
GUI Avalonia, com saída aproveitável em pipe e código de saída utilizável em
script.

## Context

O app hoje é exclusivamente Avalonia desktop (`Skat.KawkaProject.UI`,
`StartWithClassicDesktopLifetime`). Num host sem servidor gráfico ele não
sobe. Ao mesmo tempo, a camada que importa já é agnóstica de apresentação:
`Skat.KawkaProject.Core` define as interfaces de serviço e os modelos, e
`Skat.KawkaProject.Kafka` as implementa sobre o `AdminClient` da
Confluent.Kafka, sem nenhuma dependência de UI.

Nota de investigação: as ViewModels em `Features.*` são, no código,
Avalonia-free (nenhum `using Avalonia`; dependem apenas de ReactiveUI, que é
agnóstico de plataforma). Isso torna o reuso delas *tecnicamente* possível, e
essa possibilidade foi avaliada e descartada — ver "Decisões registradas".

## Layout de referência

O layout escolhido é o do Claude Code em modo texto: transcrito rolando de
cima para baixo, caixa de input com borda no rodapé, comandos digitados.

```
╭────────────────────────────────────────────╮
│  kawka · prod-cluster                      │
╰────────────────────────────────────────────╯

> topics

  NAME            PARTS   RF
  orders              4    3
  payments            8    3
  audit-log          12    3

> describe orders

  orders · 4 partitions · RF 3
  retention.ms = 604800000

  P   LEADER   EARLIEST   LATEST
  0        1          0    1,204
  1        2          0      987

╭────────────────────────────────────────────╮
│ > _                                        │
╰────────────────────────────────────────────╯
  ⏎ enviar   /help   ctrl+c sair
```

Layouts de painéis full-screen (estilo k9s/lazygit) foram considerados e
descartados: exigem posse da tela inteira, o que conflita com o requisito de
saída aproveitável em pipe.

## Escopo

**Dentro da v1:**

- Perfis de conexão: listar, conectar, desconectar
- Tópicos (leitura): listar com filtro, descrever com partições/offsets/configs
- Tópicos (administração): criar, deletar, aumentar partições, recriar com menos partições
- Mensagens: consumir por partição/offset, produzir
- Cluster: brokers, consumer groups, lag por grupo
- Dois modos de execução: REPL interativo e one-shot para scripts

**Fora da v1:**

- Edição de perfis de conexão pela TUI (use o GUI ou edite o arquivo de perfis)
- Gerenciamento de ACLs
- Edição de configuração de tópico fora do recreate
- Qualquer layout de painéis ou navegação por setas entre listas

## Arquitetura

Projeto novo `Skat.KawkaProject.Tui` (executável console, `net10.0`),
referenciando **apenas** `Skat.KawkaProject.Core` e
`Skat.KawkaProject.Kafka`. Pacote novo: `Spectre.Console` (versão fixada no
plano de implementação).

Quatro camadas, sustentadas por uma regra: **comandos devolvem dados, não
pixels.**

| Camada | Responsabilidade | Toca `Console` |
|---|---|---|
| Commands | Parseia argumentos, chama serviços, devolve `CommandResult` | Não |
| Rendering | Transforma `CommandResult` em tabela Spectre ou texto puro | Escrita |
| Safety | Gate de confirmação de operações destrutivas | Via `IConfirmer` |
| Shell | Loop REPL, caixa de prompt, histórico, Ctrl+C | Leitura |

Contratos centrais:

```csharp
public interface ITuiCommand
{
    string Name { get; }
    string Usage { get; }
    string Summary { get; }
    bool RequiresSession { get; }
    Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct);
}

public sealed class CommandContext
{
    public IReadOnlyList<string> Args { get; init; }
    public IReadOnlyDictionary<string, string?> Flags { get; init; }
    public IKafkaSession? Session { get; init; }
    public IConfirmer Confirmer { get; init; }
}

public abstract record CommandResult
{
    public sealed record Table(string? Title, IReadOnlyList<string> Columns,
                               IReadOnlyList<IReadOnlyList<string>> Rows) : CommandResult;
    public sealed record Pairs(string? Title, IReadOnlyDictionary<string, string> Values) : CommandResult;
    public sealed record Text(string Message) : CommandResult;
    public sealed record Failure(string Message, int ExitCode) : CommandResult;
}
```

`Program.cs` escolhe o modo: com argumentos, executa um comando e sai com o
`ExitCode`; sem argumentos, abre o REPL. A capacidade interativa do console
(`AnsiConsole.Profile.Capabilities.Interactive`) decide qual
`IResultRenderer` é injetado — a ausência de TTY é resolvida uma vez, na
composição, e não com condicionais espalhados.

## Estrutura de arquivos

```
Skat.KawkaProject.Tui/
  Program.cs                 — entry: argv → one-shot ou REPL, composição
  TuiHost.cs                 — loop REPL: prompt, dispatch, Ctrl+C
  ExitCodes.cs               — constantes dos códigos de saída
  Input/
    IKeySource.cs            — abstração de leitura de teclas (testabilidade)
    ConsoleKeySource.cs      — implementação real
    PromptReader.cs          — caixa com borda, setas, backspace, redesenho
    LineHistory.cs           — histórico em memória + ~/.kawka/history
  Commands/
    ITuiCommand.cs
    CommandContext.cs
    CommandResult.cs
    CommandRegistry.cs       — nome → handler; gera `help` a partir de Usage/Summary
    ArgumentParser.cs        — linha → verbo + args + flags (com aspas)
    ConnectionCommands.cs    — profiles, connect, disconnect, status
    TopicCommands.cs         — topics, describe
    TopicAdminCommands.cs    — create, delete, increase, recreate
    MessageCommands.cs       — consume, produce
    ClusterCommands.cs       — brokers, groups, lag
  Rendering/
    IResultRenderer.cs
    SpectreRenderer.cs       — TTY: tabelas, cores, painéis
    PlainTextRenderer.cs     — sem TTY: colunas separadas por tab, sem ANSI
  Safety/
    IConfirmer.cs
    InteractiveConfirmer.cs
    NonInteractiveConfirmer.cs

Skat.KawkaProject.Tui.Tests/
```

## Superfície de comandos

| Comando | Sessão | Descrição |
|---|---|---|
| `profiles` | não | Lista os perfis de conexão salvos |
| `connect <perfil>` | não | Abre uma sessão contra o perfil |
| `disconnect` | sim | Fecha a sessão ativa |
| `status` | não | Mostra a conexão ativa, se houver |
| `topics [filtro]` | sim | Lista tópicos, opcionalmente filtrando por substring |
| `describe <tópico>` | sim | Partições, offsets, líder, RF e overrides de config |
| `create <tópico> --partitions N [--replication N]` | sim | Cria um tópico |
| `delete <tópico>` | sim | **Destrutivo.** Deleta um tópico |
| `increase <tópico> --to N` | sim | Aumenta a contagem de partições |
| `recreate <tópico> --to N` | sim | **Destrutivo.** Delete + recreate com menos partições |
| `consume <tópico> [--partition N] [--from earliest\|latest\|<offset>] [--limit N]` | sim | Lê mensagens |
| `produce <tópico> [--partition N] [--key K] --value V` | sim | Publica uma mensagem |
| `brokers` | sim | Lista os brokers do cluster |
| `groups` | sim | Lista consumer groups |
| `lag <grupo>` | sim | Lag por partição do grupo |
| `help [comando]` | não | Ajuda geral ou de um comando |
| `exit` / `quit` | não | Sai do REPL |

Flags globais válidas em modo one-shot: `--profile <nome>` (obrigatória para
comandos que exigem sessão), `--yes-i-know-this-deletes-data`, `--no-color`,
`--output text|tsv`.

`--output` seleciona o renderer explicitamente, sobrepondo a detecção de TTY:
`text` produz colunas alinhadas legíveis por humano (o padrão com TTY), `tsv`
produz campos separados por tab sem cabeçalho decorado, próprio para `cut` e
`awk` (o padrão sem TTY). `--no-color` mantém o layout de `text` mas remove
as sequências ANSI.

## Confirmação de operações destrutivas

No GUI a proteção é visual: borda vermelha, texto de aviso e digitar o nome
do tópico. No terminal parte disso não existe, então o gate é explícito:

```csharp
public interface IConfirmer
{
    Task<bool> ConfirmAsync(DestructiveAction action, CancellationToken ct);
}
```

`DestructiveAction` **não é declarado pela TUI**: reusa
`Skat.KawkaProject.Core.Models.DestructiveAction`, criado em 2026-07-25 como o
lar único do "o que se perde" (record `(TopicName, Verb, WhatIsLost,
WhatIsPreserved)`, com a factory canônica `DestructiveAction.Recreate(topico)`).
O GUI já lê dele; a TUI passa o mesmo record ao `IConfirmer`. Redeclarar o tipo
aqui reintroduziria a divergência que a centralização fechou.

- **`InteractiveConfirmer`** — imprime o bloco de aviso listando `WhatIsLost`
  e exige que o usuário digite o nome exato do tópico. Espelha o gate do GUI.
  Divergência entre o digitado e o nome do tópico é recusa, não nova tentativa
  infinita: uma tentativa, e o comando aborta.
- **`NonInteractiveConfirmer`** — em modo one-shot ou sem TTY, **recusa por
  padrão** com `ExitCodes.ConfirmationRefused`. Só prossegue quando a flag
  `--yes-i-know-this-deletes-data` está presente.

Justificativa: sem humano para digitar o nome, a resposta correta não é
assumir consentimento. Um `cron` mal configurado não pode apagar um tópico
por causa de um argumento errado. A verbosidade da flag é deliberada.

`WhatIsLost` para `recreate` vem de `DestructiveAction.RecreateLoses`: todas as
mensagens do tópico e os offsets commitados de consumer groups (que passam a
apontar para posições sem significado). **ACLs NÃO entram na lista** — ACLs
literais no mesmo nome de tópico sobrevivem ao delete+recreate, e afirmar o
contrário mandaria o usuário reconceder permissões que nunca foram revogadas.
Overrides de config são preservados e isso também é dito (via
`WhatIsPreserved`), para que a lista não seja lida como "perde tudo".

## Fluxo de dados

```
one-shot:  argv ──┐
                  ├─→ ArgumentParser → CommandRegistry.Resolve(verbo)
REPL:  PromptReader ┘        ↓
                     CommandContext { Args, Flags, Session?, Confirmer }
                             ↓
                     ITuiCommand.ExecuteAsync(ctx, ct)
                             ↓
                        CommandResult
                             ↓
              IResultRenderer.Render → stdout / stderr → exit code
```

**Ciclo de vida da sessão.** No REPL, `connect <perfil>` cria um
`IKafkaSession` que o `TuiHost` mantém; comandos seguintes o recebem pelo
`CommandContext`. Um `connect` com sessão já aberta descarta a anterior antes
de abrir a nova. No one-shot, `--profile` cria a sessão para aquele comando e
a descarta ao sair.

Comandos com `RequiresSession == true` são curto-circuitados pelo registry
antes de executar, para que nenhum handler precise checar nulo:

```csharp
if (command.RequiresSession && ctx.Session is null)
    return new CommandResult.Failure(
        "No active connection. Use 'connect <profile>' first.", ExitCodes.Usage);
```

## Tratamento de erro

**Uma única fronteira captura `Exception`: o dispatcher.** Comandos
individuais deixam exceções subir — é isso que impede cada handler de
inventar seu próprio formato de mensagem.

| Classe | Origem | Tratamento | Exit |
|---|---|---|---|
| Sucesso | — | renderiza o resultado | 0 |
| Falha operacional | broker inacessível, tópico inexistente | `Failure` com a mensagem da exceção | 1 |
| Erro de uso | verbo inexistente, argumento faltando, sem sessão | `Failure` + linha de `Usage`, sem stack trace | 2 |
| Confirmação recusada | nome divergente, ou flag ausente em modo não-interativo | `Failure` explicando qual dos dois | 3 |

**Caso especial — risco de perda de dados.** Quando o dispatcher captura uma
`TopicRecreateFailedException` com `TopicMayBeDeleted == true`, o renderer
imprime o aviso e, junto, a `PreservedConfig` completa em formato copiável.
A razão é específica do terminal: o scrollback é o único registro que o
usuário terá da configuração do tópico destruído. Este é o equivalente TUI da
Task 5 do plano de hardening, e é o motivo direto do sequenciamento descrito
abaixo.

**Ctrl+C** segue o comportamento do Claude Code: durante a execução de um
comando, cancela via `CancellationToken` e devolve o prompt; num prompt
vazio, encerra o REPL. Falha de comando nunca derruba o loop.

## Estratégia de testes

A camada pura de comandos existe para que a maior parte da suíte não veja um
terminal:

1. **Comandos** — xUnit + Moq sobre `ITopicService`, `IMessageService`,
   `IClusterService`, `IConnectionProfileRepository`. Asserção sobre o
   `CommandResult` devolvido. É o grosso da suíte.
2. **Confirmadores** — os testes mais importantes do projeto:
   `NonInteractiveConfirmer` recusa sem a flag e aceita com ela;
   `InteractiveConfirmer` rejeita nome divergente e aceita nome exato.
3. **Renderers** — Spectre permite criar um console escrevendo em
   `StringWriter`, permitindo assertar a saída exata, incluindo o caminho
   `PlainTextRenderer` sem ANSI.
4. **Parser** — linha → verbo + args + flags, cobrindo aspas e valores com
   espaço.
5. **`PromptReader`** — a leitura de teclado fica atrás de `IKeySource`, então
   o teste alimenta uma sequência roteirizada de teclas e assere a linha
   resultante e o estado do histórico.
6. **Integração** — reusa o padrão `Testcontainers.Kafka` de
   `Skat.KawkaProject.Kafka.Tests` para dois ou três comandos ponta a ponta
   contra um broker real.

**Não será feito:** teste de snapshot do frame completo da TUI. Quebra a cada
ajuste de espaçamento e não prova comportamento. As asserções são sobre
estrutura, com exceção de poucos testes focados de renderer.

## Dependências e sequenciamento

Este trabalho deve vir **depois das Tasks 1–4 do plano
`2026-07-24-topic-recreate-hardening.md`**. Não é preferência de ordem: essas
tasks movem a validação de contagem de partições e a semântica de falha da
`TopicsViewModel` para dentro de `TopicService` e da
`TopicRecreateFailedException`. Antes disso, a TUI precisaria duplicar as
regras de validação e as mensagens de perda de dados — e duas cópias de uma
regra de segurança divergem.

Dependência de pacote nova: `Spectre.Console`, apenas no projeto TUI e no seu
projeto de testes. Nenhum projeto existente ganha referência nova.

## Faseamento da implementação

O escopo da v1 é grande para um único ciclo. O plano de implementação deve
organizá-lo em fases onde **cada fase termina com software funcionando e
testável**, permitindo parar entre elas sem deixar algo pela metade:

1. **Esqueleto e leitura** — projeto, `ArgumentParser`, `CommandRegistry`,
   `CommandResult`, os dois renderers, `Program` com one-shot e REPL, e os
   comandos de conexão (`profiles`, `connect`, `disconnect`, `status`) mais
   `topics` e `describe`. Ao fim desta fase a ferramenta já é útil por SSH.
2. **Caixa de prompt** — `IKeySource`, `PromptReader`, `LineHistory`. Até
   aqui o REPL usa leitura de linha simples; esta fase entrega a caixa com
   borda, setas e histórico. Isolada de propósito, por ser a parte mais
   trabalhosa e a de menor risco funcional.
3. **Cluster e mensagens** — `brokers`, `groups`, `lag`, `consume`,
   `produce`.
4. **Operações destrutivas** — `Safety/` completo (`IConfirmer` e as duas
   implementações) e então `create`, `delete`, `increase`, `recreate`. Última
   de propósito: entra depois de a suíte de testes e os renderers estarem
   maduros, e depende das Tasks 1–4 do hardening.

## Decisões registradas

**Não reaproveitar as ViewModels de `Features.*`.** Elas são
tecnicamente Avalonia-free, mas moldadas para GUI: carregam `IScreen` e
roteamento, `Interaction<>` que exige handler registrado,
`ObservableCollection`, flags `IsBusy`, e setters com efeito colateral
fire-and-forget (`SelectedTopic` dispara um carregamento em background). Num
REPL, tudo isso é atrito a contornar. A TUI e o GUI se encontram nos serviços
do `Core`. Custo aceito: os formatos de exibição das duas interfaces podem
divergir com o tempo.

**Spectre.Console em vez de Terminal.Gui.** Terminal.Gui é a escolha correta
para layout de painéis, que foi descartado. Para transcrito rolante, ele quer
posse da tela inteira, conflitando com saída em pipe. Spectre entrega tabelas,
cores e detecção de não-TTY, que é o requisito de scriptabilidade resolvido
sem código condicional.

**Operações destrutivas incluídas na v1.** Foi levantada a ressalva de que a
revisão recente encontrou doze defeitos nesse caminho e de que o terminal
perde o gate visual do GUI; a decisão de incluí-las mesmo assim foi tomada
com essa informação. O desenho responde com o `IConfirmer` acima e com o
sequenciamento após o hardening.

**Custo conhecido e aceito.** A caixa de input com borda, histórico e
navegação por setas não vem pronta do Spectre — o `TextPrompt` dele é uma
linha simples. `PromptReader` é código próprio, estimado na ordem de 150
linhas, e é o componente mais trabalhoso do projeto.
