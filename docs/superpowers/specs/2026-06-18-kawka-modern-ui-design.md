# Kawka — Modern UI Design Spec

## Goal

Substituir o tema Fluent padrão por uma linguagem visual coesa e moderna: dark/light com toggle, paleta Catppuccin, acento Azul Kafka, sidebar em árvore modernizada, tabelas com cabeçalhos, e status bar azul com contexto da sessão.

## Decisões de Design

| Decisão | Escolha |
|---|---|
| Tema | Dark + Light com toggle em runtime |
| Cor de acento | `#0088CC` (Azul Kafka) |
| Estilo de sidebar | Árvore modernizada (C) — mesma estrutura, com ícones, status badges e ações inline |
| Densidade | Compacta — linhas de 28 px, fonte 11–12 px |

---

## 1. Paleta de Cores

### Dark (Catppuccin Mocha)

| Token | Hex | Uso |
|---|---|---|
| `Background` | `#1e1e2e` | Área principal de conteúdo |
| `Surface` | `#181825` | Sidebar, titlebar, cabeçalhos de tabela |
| `SurfaceDeep` | `#11111b` | Cabeçalho de tabela sticky |
| `Border` | `#313244` | Bordas, separadores |
| `BorderSubtle` | `#2a2a3d` | Bordas internas da sidebar |
| `TextPrimary` | `#cdd6f4` | Texto principal |
| `TextMuted` | `#7f849c` | Texto secundário, labels |
| `TextFaint` | `#45475a` | Section labels, placeholders |
| `Accent` | `#0088CC` | Botão primário, seleção, status bar |
| `AccentSubtle` | `rgba(0,136,204,0.12)` | Linha selecionada, botão primary hover |
| `StatusLive` | `#a6e3a1` | Dot verde — conexão ativa |
| `StatusConnecting` | `#f9e2af` | Dot âmbar — conectando |
| `StatusError` | `#f38ba8` | Texto de erro, botão destrutivo |
| `StatusOff` | `#45475a` | Dot cinza — desconectado |

### Light (Catppuccin Latte)

| Token | Hex | Uso |
|---|---|---|
| `Background` | `#eff1f5` | Área principal |
| `Surface` | `#e6e9ef` | Sidebar, titlebar |
| `SurfaceDeep` | `#dce0e8` | Cabeçalho de tabela |
| `Border` | `#bcc0cc` | Bordas |
| `BorderSubtle` | `#ccd0da` | Bordas internas |
| `TextPrimary` | `#4c4f69` | Texto principal |
| `TextMuted` | `#8c8fa1` | Texto secundário |
| `TextFaint` | `#9ca0b0` | Section labels |
| `Accent` | `#0088CC` | Igual ao dark |
| `AccentSubtle` | `rgba(0,136,204,0.08)` | Linha selecionada |
| `StatusLive` | `#40a02b` | Verde — conexão ativa |
| `StatusConnecting` | `#df8e1d` | Âmbar — conectando |
| `StatusError` | `#d20f39` | Erro |
| `StatusOff` | `#9ca0b0` | Desconectado |

---

## 2. Tipografia

- **Fonte UI:** `Segoe UI` → `system-ui` → `-apple-system` → `sans-serif`
- **Fonte mono:** `Cascadia Code` → `JetBrains Mono` → `Consolas` → `monospace`
- **Tamanhos:**
  - Section labels: 9 px, uppercase, `letter-spacing: 1px`, `font-weight: 700`
  - Itens de lista / células: 11.5 px
  - Cabeçalhos de coluna: 10 px, uppercase, `letter-spacing: 0.8px`, `font-weight: 700`
  - Texto de ação / badge: 10 px
  - Status bar: 10 px

---

## 3. Titlebar

- Altura: 36 px
- Fundo: `SurfaceDeep`
- Conteúdo: três dots macOS (🔴🟡🟢) à esquerda + título centralizado em `TextFaint` + botão de toggle de tema à direita
- Toggle de tema: pill com `🌙 Dark` / `☀ Light`, fundo `Surface`, borda `Border`
- Faz parte da `MainWindow` (não é um UserControl separado)
- Armazena e persiste `RequestedThemeVariant` via `Application.Current.RequestedThemeVariant`

---

## 4. Sidebar — Árvore Modernizada

**Largura:** 230 px · **Fundo:** `Surface`

### Toolbar (topo da sidebar)
- Botão primário "＋ Add Connection": fundo `AccentSubtle`, borda `Accent` 30% opacidade, texto `Accent`
- Botão ícone "↺ Refresh": fundo transparente, borda `Border`, ícone `TextFaint`

### Seção "Connections"
- Label: 9 px uppercase `TextFaint`

### Nó de conexão (`ConnectionNodeViewModel`)
- **Header** (linha clicável, 32 px):
  - Status dot (7 × 7 px, círculo): `StatusLive` com glow / `StatusConnecting` com glow / `StatusOff` sem glow
  - Nome da conexão (12 px, `TextPrimary`, `font-weight: 500`)
  - Status badge (pill 9 px): `live` verde / `conn…` âmbar / `off` cinza — empurrado para direita com `margin-left: auto`
- **Ações inline** (visíveis quando conectado, abaixo do header, indentado 28 px):
  - Chips: `📋 Topics` · `✉ Msgs` · `🖥 Cluster`
  - Chip ativo: fundo `AccentSubtle`, texto `Accent`, `font-weight: 600`
  - Chip inativo: texto `TextMuted`, hover fundo `Border`

### Rodapé da sidebar
- Borda superior `BorderSubtle`
- Links "⚙ Settings · ? Docs" em 10 px `TextFaint`

---

## 5. Toolbar de Conteúdo

- Altura: 34 px · Fundo: `Surface` · Borda inferior: `BorderSubtle`
- **Breadcrumb** (esquerda): `TextMuted › TextPrimary` — ex: `prod-cluster › 📋 Topics`
- **Separador vertical** 1 px `Border`
- **Campo de filtro**: fundo `SurfaceDeep`, borda `Border`, placeholder `TextFaint`, 160 px de largura
- **Botões** (direita):
  - Secundário (Refresh): fundo `Surface2`, borda `Border`, texto `TextPrimary`
  - Primário (Create / Fetch): fundo `AccentSubtle`, borda `Accent` 40%, texto `Accent`

---

## 6. Tabelas de Dados

- Cabeçalho sticky: fundo `SurfaceDeep`, borda inferior `Border`
  - Células `<th>`: 10 px uppercase, `font-weight: 700`, cor `TextMuted`, `letter-spacing: 0.8px`, padding `5px 12px`
- Linhas: 28 px de altura, borda inferior `Border` 60% opacidade
  - Hover: fundo `#262637` (dark) / `#dce0e8` (light)
  - Selecionada: fundo `AccentSubtle`
- Células:
  - Nomes de tópico / chave: fonte mono, cor `Accent`
  - Números (partições, offsets): cor `Accent`, `font-weight: 600`
  - Texto secundário (replication factor, host): cor `TextMuted`
- **Status badges** (pill):
  - `✓ healthy`: verde `StatusLive` 12% opacidade de fundo
  - `⚠ lag`: âmbar `StatusConnecting` 12% opacidade
  - `✕ error`: vermelho `StatusError` 12% opacidade

---

## 7. Painel de Detalhe (lateral direito)

Exibido na view de Topics quando um tópico está selecionado.

- Largura: 260 px · Fundo: `Surface` · Borda esquerda: `Border`
- **Header:** nome do tópico, 11 px bold, ícone à esquerda
- **Seções:** "Properties" e "Partitions" — labels 9 px uppercase `TextFaint`
- **Linhas de propriedade:** chave `TextMuted` / valor `TextPrimary`; valores mono em `#89b4fa` (azul suave)
- **Linhas de partição:** ID (cinza) · range de offsets (mono) · total de mensagens (verde)
- **Rodapé de ações:** botões "✉ Browse" (secundário) e "🗑 Delete" (tom vermelho)

---

## 8. Status Bar

- Altura: 22 px · Fundo: `Accent` (#0088CC)
- Texto: `rgba(255,255,255,0.9)`, 10 px
- Conteúdo (esquerda): dot colorido (usando `ConnectionStatusConverter`) + `<nome-da-conexão>` · separador · contagem de itens + filtro ativo (quando aplicável)
- Conteúdo (direita): latência da última operação (`⚡ 48ms`)

---

## 9. Barra de Progresso de Carregamento

- Altura: 4 px, `IsIndeterminate=True`, cor `Accent`
- Posicionada no topo do conteúdo (`DockPanel.Dock="Top"`)
- `IsVisible` ligado ao `IsBusy` do ViewModel (padrão já existente)

---

## 10. Implementação — Abordagem Técnica

### ResourceDictionary com ThemeVariant

Criar um arquivo de recursos por variante de tema:

```
src/Skat.KawkaProject.UI/
  Assets/
    Themes/
      DarkTheme.axaml     ← ThemeVariant.Dark resources
      LightTheme.axaml    ← ThemeVariant.Light resources
  Styles/
    AppStyles.axaml       ← Estilos globais de controle (Button, TextBox, ListBox…)
```

Em `App.axaml`:
```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.ThemeDictionaries>
      <ResourceDictionary x:Key="Dark">
        <ResourceDictionary.MergedDictionaries>
          <ResourceInclude Source="/Assets/Themes/DarkTheme.axaml" />
        </ResourceDictionary.MergedDictionaries>
      </ResourceDictionary>
      <ResourceDictionary x:Key="Light">
        <ResourceDictionary.MergedDictionaries>
          <ResourceInclude Source="/Assets/Themes/LightTheme.axaml" />
        </ResourceDictionary.MergedDictionaries>
      </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
  </ResourceDictionary>
</Application.Resources>
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="/Styles/AppStyles.axaml" />
</Application.Styles>
```

### Toggle de Tema em Runtime

Em `ShellViewModel`:
```csharp
public void ToggleTheme()
{
    var app = Application.Current!;
    app.RequestedThemeVariant = app.RequestedThemeVariant == ThemeVariant.Dark
        ? ThemeVariant.Light
        : ThemeVariant.Dark;
}
```

### Escopo das Mudanças

Todos os arquivos AXAML existentes recebem atualização de estilo. Nenhum ViewModel é alterado (exceto `ShellViewModel` para o toggle). Nenhuma interface do `Core` muda.

Arquivos modificados:
- `App.axaml` — adiciona ResourceDictionary + StyleInclude
- `MainWindow.axaml` — adiciona titlebar com toggle
- `SidebarView.axaml` — aplica novo estilo (status dot com glow, badges, ações inline)
- `TopicsView.axaml` — cabeçalhos de coluna + status badges + painel de detalhe
- `MessagesView.axaml` — cabeçalhos de coluna + status bar
- `ClusterView.axaml` — cabeçalhos de coluna + status bar
- `ConnectionEditorView.axaml` — estilo de formulário: fundo `Surface`, labels `TextMuted`, inputs com borda `Border`, botão Save primário

Arquivos criados:
- `Assets/Themes/DarkTheme.axaml`
- `Assets/Themes/LightTheme.axaml`
- `Styles/AppStyles.axaml`
- Método `ToggleTheme()` e propriedade `IsDarkTheme` adicionados a `ShellViewModel` (sem ViewModel separado)
