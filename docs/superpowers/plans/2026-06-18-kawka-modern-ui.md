# Kawka Modern UI Design — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the Catppuccin-based dark/light design system to all Kawka views — themed color tokens, compact tables, modernized sidebar, and a status bar in each feature view.

**Architecture:** Three new files provide the theme infrastructure (DarkTheme.axaml, LightTheme.axaml, AppStyles.axaml), all registered in App.axaml. Every AXAML view is rewritten to consume these tokens via `DynamicResource`. Minor ViewModel additions (StatusText, IsConnected, StatusLabel) support the new UI elements. No Core interfaces or Kafka implementations are modified.

**Tech Stack:** Avalonia 11.3.9, FluentTheme, ReactiveUI 20.1.1, .NET 10

## Global Constraints

- Target framework: `net10.0`
- No new NuGet packages — use only packages already in `Skat.KawkaProject.UI.csproj`
- All theme colors use `DynamicResource` (not `StaticResource`) so runtime theme toggle works
- Accent color is `#0088CC` in both themes — identical value
- No changes to `Core` interfaces, models, or `Kafka` implementations
- All new `.axaml` files must be declared as `AvaloniaResource` in the UI `.csproj`
- FluentTheme remains; AppStyles is loaded **after** it so selectors win
- `x:DataType` on existing views must be preserved (compiled binding intact)
- Inner `DataTemplate` blocks that don't already have `DataType` keep using reflection bindings — no `DataType` should be added to them
- The `ConnectionEditorView` is a `Window`, not a `UserControl`
- Build verification command: `dotnet build src/Skat.KawkaProject.sln -c Release --no-restore`
- No unit tests for pure styling — build verification is the test gate for AXAML tasks. ViewModel property additions (StatusText, IsConnected etc.) are verified by running the full test suite: `dotnet test src/Skat.KawkaProject.sln`

---

## File Map

**New files:**
| File | Responsibility |
|------|---------------|
| `src/Skat.KawkaProject.UI/Assets/Themes/DarkTheme.axaml` | All dark (Catppuccin Mocha) SolidColorBrush tokens |
| `src/Skat.KawkaProject.UI/Assets/Themes/LightTheme.axaml` | All light (Catppuccin Latte) SolidColorBrush tokens |
| `src/Skat.KawkaProject.UI/Styles/AppStyles.axaml` | Global control style overrides: TextBlock, Button, ListBox, ListBoxItem, ProgressBar, ComboBox, NumericUpDown, TabItem |

**Modified files:**
| File | Change |
|------|--------|
| `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj` | Add `<AvaloniaResource Include="Styles\**" />` |
| `src/Skat.KawkaProject.UI/App.axaml` | ThemeDictionaries wired, `RequestedThemeVariant="Dark"`, StyleInclude for AppStyles |
| `src/Skat.KawkaProject.UI/ViewModels/ShellViewModel.cs` | Add `ToggleThemeCommand`, `ThemeLabel` |
| `src/Skat.KawkaProject.UI/Views/MainWindow.axaml` | Header bar with title + theme toggle; sidebar width 230 px; font family set |
| `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs` | Add `IsConnected`, `IsDisconnected`, `StatusLabel`; raise them on `Status` change |
| `src/Skat.KawkaProject.UI/ViewModels/ConnectionStatusConverter.cs` | Update colors to Catppuccin; add badge mode via `ConverterParameter="badge"` |
| `src/Skat.KawkaProject.UI/Views/SidebarView.axaml` | Remove Expander; modernized tree layout with status dots, badges, inline action chips |
| `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs` | Add `StatusText`; raise in `ApplyFilter()` |
| `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml` | Column headers, compact rows, status bar, styled error bar, styled detail panel |
| `src/Skat.KawkaProject.Features.Messages/ViewModels/MessagesViewModel.cs` | Add `StatusText`; raise via `Messages.CollectionChanged` |
| `src/Skat.KawkaProject.Features.Messages/Views/MessagesView.axaml` | Column headers, compact rows, status bar, styled toolbar and error bar |
| `src/Skat.KawkaProject.Features.Cluster/ViewModels/ClusterViewModel.cs` | Add `StatusText`; raise via `Brokers.CollectionChanged` |
| `src/Skat.KawkaProject.Features.Cluster/Views/ClusterView.axaml` | Column headers per tab, status bar, styled toolbar and error bar |
| `src/Skat.KawkaProject.Features.Connections/Views/ConnectionEditorView.axaml` | Surface background, muted labels, styled inputs, primary Save button |

---

## Task 1: Theme Infrastructure

**Files:**
- Create: `src/Skat.KawkaProject.UI/Assets/Themes/DarkTheme.axaml`
- Create: `src/Skat.KawkaProject.UI/Assets/Themes/LightTheme.axaml`
- Create: `src/Skat.KawkaProject.UI/Styles/AppStyles.axaml`
- Modify: `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`
- Modify: `src/Skat.KawkaProject.UI/App.axaml`

**Interfaces:**
- Produces: Resource keys (`BackgroundBrush`, `SurfaceBrush`, `SurfaceDeepBrush`, `BorderBrush`, `BorderSubtleBrush`, `TextPrimaryBrush`, `TextMutedBrush`, `TextFaintBrush`, `AccentBrush`, `AccentSubtleBrush`, `StatusLiveBrush`, `StatusConnBrush`, `StatusErrorBrush`, `StatusOffBrush`, `RowHoverBrush`, `DestructiveBrush`, `DestructiveTextBrush`) available to ALL views via `DynamicResource`

- [ ] **Step 1: Create DarkTheme.axaml**

`src/Skat.KawkaProject.UI/Assets/Themes/DarkTheme.axaml`
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="BackgroundBrush"       Color="#1e1e2e" />
    <SolidColorBrush x:Key="SurfaceBrush"          Color="#181825" />
    <SolidColorBrush x:Key="SurfaceDeepBrush"      Color="#11111b" />
    <SolidColorBrush x:Key="BorderBrush"           Color="#313244" />
    <SolidColorBrush x:Key="BorderSubtleBrush"     Color="#2a2a3d" />
    <SolidColorBrush x:Key="TextPrimaryBrush"      Color="#cdd6f4" />
    <SolidColorBrush x:Key="TextMutedBrush"        Color="#7f849c" />
    <SolidColorBrush x:Key="TextFaintBrush"        Color="#45475a" />
    <SolidColorBrush x:Key="AccentBrush"           Color="#0088CC" />
    <SolidColorBrush x:Key="AccentSubtleBrush"     Color="#1F0088CC" />
    <SolidColorBrush x:Key="StatusLiveBrush"       Color="#a6e3a1" />
    <SolidColorBrush x:Key="StatusConnBrush"       Color="#f9e2af" />
    <SolidColorBrush x:Key="StatusErrorBrush"      Color="#f38ba8" />
    <SolidColorBrush x:Key="StatusOffBrush"        Color="#45475a" />
    <SolidColorBrush x:Key="StatusLiveBadgeBrush"  Color="#1Fa6e3a1" />
    <SolidColorBrush x:Key="StatusConnBadgeBrush"  Color="#1Ff9e2af" />
    <SolidColorBrush x:Key="StatusErrorBadgeBrush" Color="#1Ff38ba8" />
    <SolidColorBrush x:Key="StatusOffBadgeBrush"   Color="#1F45475a" />
    <SolidColorBrush x:Key="RowHoverBrush"         Color="#262637" />
    <SolidColorBrush x:Key="DestructiveBrush"      Color="#1Af38ba8" />
    <SolidColorBrush x:Key="DestructiveTextBrush"  Color="#f38ba8" />
</ResourceDictionary>
```

- [ ] **Step 2: Create LightTheme.axaml**

`src/Skat.KawkaProject.UI/Assets/Themes/LightTheme.axaml`
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="BackgroundBrush"       Color="#eff1f5" />
    <SolidColorBrush x:Key="SurfaceBrush"          Color="#e6e9ef" />
    <SolidColorBrush x:Key="SurfaceDeepBrush"      Color="#dce0e8" />
    <SolidColorBrush x:Key="BorderBrush"           Color="#bcc0cc" />
    <SolidColorBrush x:Key="BorderSubtleBrush"     Color="#ccd0da" />
    <SolidColorBrush x:Key="TextPrimaryBrush"      Color="#4c4f69" />
    <SolidColorBrush x:Key="TextMutedBrush"        Color="#8c8fa1" />
    <SolidColorBrush x:Key="TextFaintBrush"        Color="#9ca0b0" />
    <SolidColorBrush x:Key="AccentBrush"           Color="#0088CC" />
    <SolidColorBrush x:Key="AccentSubtleBrush"     Color="#140088CC" />
    <SolidColorBrush x:Key="StatusLiveBrush"       Color="#40a02b" />
    <SolidColorBrush x:Key="StatusConnBrush"       Color="#df8e1d" />
    <SolidColorBrush x:Key="StatusErrorBrush"      Color="#d20f39" />
    <SolidColorBrush x:Key="StatusOffBrush"        Color="#9ca0b0" />
    <SolidColorBrush x:Key="StatusLiveBadgeBrush"  Color="#2840a02b" />
    <SolidColorBrush x:Key="StatusConnBadgeBrush"  Color="#28df8e1d" />
    <SolidColorBrush x:Key="StatusErrorBadgeBrush" Color="#28d20f39" />
    <SolidColorBrush x:Key="StatusOffBadgeBrush"   Color="#289ca0b0" />
    <SolidColorBrush x:Key="RowHoverBrush"         Color="#dce0e8" />
    <SolidColorBrush x:Key="DestructiveBrush"      Color="#1Ad20f39" />
    <SolidColorBrush x:Key="DestructiveTextBrush"  Color="#d20f39" />
</ResourceDictionary>
```

- [ ] **Step 3: Create AppStyles.axaml**

`src/Skat.KawkaProject.UI/Styles/AppStyles.axaml`
```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Style Selector="TextBlock">
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
    </Style>

    <Style Selector="TextBox">
        <Setter Property="Background" Value="{DynamicResource SurfaceDeepBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="5" />
    </Style>

    <Style Selector="Button">
        <Setter Property="Background" Value="{DynamicResource SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="5" />
        <Setter Property="Padding" Value="8,4" />
    </Style>

    <Style Selector="ListBox">
        <Setter Property="Background" Value="{DynamicResource BackgroundBrush}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="0" />
    </Style>

    <Style Selector="ListBoxItem">
        <Setter Property="Padding" Value="0" />
        <Setter Property="Margin" Value="0" />
    </Style>

    <Style Selector="ListBoxItem:pointerover /template/ ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource RowHoverBrush}" />
    </Style>

    <Style Selector="ListBoxItem:selected /template/ ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource AccentSubtleBrush}" />
    </Style>

    <Style Selector="ListBoxItem:selected:pointerover /template/ ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource AccentSubtleBrush}" />
    </Style>

    <Style Selector="ProgressBar">
        <Setter Property="Foreground" Value="{DynamicResource AccentBrush}" />
        <Setter Property="Background" Value="{DynamicResource BorderBrush}" />
    </Style>

    <Style Selector="ComboBox">
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="5" />
    </Style>

    <Style Selector="NumericUpDown">
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="5" />
    </Style>

    <Style Selector="TabItem">
        <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}" />
        <Setter Property="FontSize" Value="11" />
    </Style>

    <Style Selector="TabItem:selected">
        <Setter Property="Foreground" Value="{DynamicResource AccentBrush}" />
    </Style>
</Styles>
```

- [ ] **Step 4: Update csproj to include Styles directory**

In `src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj`, add after the existing `AvaloniaResource` line:
```xml
    <AvaloniaResource Include="Assets\**" />
    <AvaloniaResource Include="Styles\**" />
```

(Replace the single existing line with these two.)

- [ ] **Step 5: Rewrite App.axaml**

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="using:Skat.KawkaProject.UI"
             x:Class="Skat.KawkaProject.UI.App"
             RequestedThemeVariant="Dark">
    <Application.DataTemplates>
        <local:ViewLocator/>
    </Application.DataTemplates>
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
</Application>
```

- [ ] **Step 6: Verify build**

```bash
dotnet build src/Skat.KawkaProject.sln -c Release --no-restore
```

Expected: Build succeeded, 0 errors. If AXAML parse errors appear, check that `ResourceInclude Source` paths are correct (`/Assets/Themes/DarkTheme.axaml` with leading slash = avares root).

- [ ] **Step 7: Commit**

```bash
git add src/Skat.KawkaProject.UI/Assets/Themes/DarkTheme.axaml \
        src/Skat.KawkaProject.UI/Assets/Themes/LightTheme.axaml \
        src/Skat.KawkaProject.UI/Styles/AppStyles.axaml \
        src/Skat.KawkaProject.UI/Skat.KawkaProject.UI.csproj \
        src/Skat.KawkaProject.UI/App.axaml
git commit -m "feat(ui): add Catppuccin theme infrastructure and global control styles"
```

---

## Task 2: Titlebar + Theme Toggle

**Files:**
- Modify: `src/Skat.KawkaProject.UI/ViewModels/ShellViewModel.cs`
- Modify: `src/Skat.KawkaProject.UI/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `DynamicResource SurfaceDeepBrush`, `BorderBrush`, `TextFaintBrush`, `SurfaceBrush`, `TextMutedBrush` (from Task 1)
- Produces: `ShellViewModel.ToggleThemeCommand` (ICommand), `ShellViewModel.ThemeLabel` (string — `"☀ Light"` when dark, `"🌙 Dark"` when light)

- [ ] **Step 1: Update ShellViewModel**

Replace `src/Skat.KawkaProject.UI/ViewModels/ShellViewModel.cs` entirely:
```csharp
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;

namespace Skat.KawkaProject.UI.ViewModels;

public class ShellViewModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new();
    public SidebarViewModel Sidebar { get; }
    public ICommand ToggleThemeCommand { get; }

    public string ThemeLabel =>
        Application.Current?.RequestedThemeVariant == ThemeVariant.Dark ? "☀ Light" : "🌙 Dark";

    public ShellViewModel(
        IConnectionProfileRepository profileRepo,
        IKafkaConnectionFactory connectionFactory,
        ITopicService topicService,
        IMessageService messageService,
        IClusterService clusterService)
    {
        Sidebar = new SidebarViewModel(this, profileRepo, connectionFactory,
            topicService, messageService, clusterService);

        ToggleThemeCommand = ReactiveCommand.Create(() =>
        {
            var app = Application.Current!;
            app.RequestedThemeVariant = app.RequestedThemeVariant == ThemeVariant.Dark
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            this.RaisePropertyChanged(nameof(ThemeLabel));
        });
    }
}
```

- [ ] **Step 2: Rewrite MainWindow.axaml**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Skat.KawkaProject.UI.ViewModels"
        xmlns:rxui="clr-namespace:Avalonia.ReactiveUI;assembly=Avalonia.ReactiveUI"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        x:Class="Skat.KawkaProject.UI.Views.MainWindow"
        x:DataType="vm:ShellViewModel"
        Width="1100" Height="700"
        Title="Kawka"
        Background="{DynamicResource BackgroundBrush}"
        FontFamily="Segoe UI,system-ui,-apple-system,sans-serif">

    <Design.DataContext>
        <vm:ShellViewModel />
    </Design.DataContext>

    <DockPanel>
        <!-- Header bar -->
        <Border DockPanel.Dock="Top" Height="36"
                Background="{DynamicResource SurfaceDeepBrush}"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="0,0,0,1">
            <Grid ColumnDefinitions="*,Auto,*">
                <TextBlock Grid.Column="1"
                           Text="Kawka — Kafka Admin"
                           FontSize="12"
                           Foreground="{DynamicResource TextFaintBrush}"
                           VerticalAlignment="Center" />
                <Button Grid.Column="2"
                        Content="{Binding ThemeLabel}"
                        Command="{Binding ToggleThemeCommand}"
                        HorizontalAlignment="Right"
                        Margin="0,0,12,0"
                        Padding="10,4"
                        FontSize="11"
                        Background="{DynamicResource SurfaceBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        Foreground="{DynamicResource TextMutedBrush}"
                        CornerRadius="12" />
            </Grid>
        </Border>

        <!-- Sidebar + content -->
        <Grid ColumnDefinitions="230,*">
            <Border Grid.Column="0"
                    Background="{DynamicResource SurfaceBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="0,0,1,0">
                <ContentControl Content="{Binding Sidebar}" />
            </Border>
            <rxui:RoutedViewHost Grid.Column="1" Router="{Binding Router}" />
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/Skat.KawkaProject.sln -c Release --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Skat.KawkaProject.UI/ViewModels/ShellViewModel.cs \
        src/Skat.KawkaProject.UI/Views/MainWindow.axaml
git commit -m "feat(ui): add header bar with theme toggle button"
```

---

## Task 3: Sidebar Redesign

**Files:**
- Modify: `src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs`
- Modify: `src/Skat.KawkaProject.UI/ViewModels/ConnectionStatusConverter.cs`
- Modify: `src/Skat.KawkaProject.UI/Views/SidebarView.axaml`

**Interfaces:**
- Consumes: `DynamicResource SurfaceBrush`, `BorderSubtleBrush`, `AccentSubtleBrush`, `AccentBrush`, `TextFaintBrush`, `TextPrimaryBrush`, `TextMutedBrush`, `StatusErrorBrush`, `DestructiveTextBrush` (Task 1)
- Produces: `ConnectionNodeViewModel.IsConnected` (bool), `ConnectionNodeViewModel.IsDisconnected` (bool), `ConnectionNodeViewModel.StatusLabel` (string)
- Produces: `ConnectionStatusConverter` — when `ConverterParameter="badge"`, returns badge-background brush (~12% alpha); otherwise returns dot-fill brush (solid)

- [ ] **Step 1: Update ConnectionNodeViewModel**

Add `IsConnected`, `IsDisconnected`, `StatusLabel` properties and raise them when `Status` changes. Full file:

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
        private set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(IsDisconnected));
            this.RaisePropertyChanged(nameof(StatusLabel));
        }
    }

    public bool IsConnected => _status == ConnectionStatus.Connected;
    public bool IsDisconnected => _status != ConnectionStatus.Connected;

    public string StatusLabel => _status switch
    {
        ConnectionStatus.Connected => "live",
        ConnectionStatus.Connecting => "conn…",
        ConnectionStatus.Error => "error",
        _ => "off"
    };

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
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Topics.ViewModels.TopicsViewModel(shell, _session, topicService));
        });

        NavigateToMessagesCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Messages.ViewModels.MessagesViewModel(shell, _session, messageService));
        });

        NavigateToClusterCommand = ReactiveCommand.Create(() =>
        {
            if (_session == null) return;
            shell.Router.Navigate.Execute(
                new Skat.KawkaProject.Features.Cluster.ViewModels.ClusterViewModel(shell, _session, clusterService));
        });

        DeleteCommand = ReactiveCommand.Create(() => onDelete(this));
    }
}
```

- [ ] **Step 2: Run tests to verify ViewModel change doesn't break anything**

```bash
dotnet test src/Skat.KawkaProject.sln --no-restore
```

Expected: All tests pass (14 total across Core.Tests, Kafka.Tests, Features.Tests).

- [ ] **Step 3: Update ConnectionStatusConverter**

Replace `src/Skat.KawkaProject.UI/ViewModels/ConnectionStatusConverter.cs` entirely:
```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Skat.KawkaProject.UI.ViewModels;

public class ConnectionStatusConverter : IValueConverter
{
    public static readonly ConnectionStatusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConnectionStatus status) return Brushes.Transparent;
        bool isBadge = parameter?.ToString() == "badge";

        return status switch
        {
            ConnectionStatus.Connected  => new SolidColorBrush(Color.Parse(isBadge ? "#1Fa6e3a1" : "#a6e3a1")),
            ConnectionStatus.Connecting => new SolidColorBrush(Color.Parse(isBadge ? "#1Ff9e2af" : "#f9e2af")),
            ConnectionStatus.Error      => new SolidColorBrush(Color.Parse(isBadge ? "#1Ff38ba8" : "#f38ba8")),
            _                           => new SolidColorBrush(Color.Parse(isBadge ? "#1F45475a" : "#45475a"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 4: Rewrite SidebarView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Skat.KawkaProject.UI.ViewModels"
             x:Class="Skat.KawkaProject.UI.Views.SidebarView"
             x:DataType="vm:SidebarViewModel"
             Background="{DynamicResource SurfaceBrush}">
    <DockPanel>
        <!-- Toolbar -->
        <Border DockPanel.Dock="Top" Padding="8"
                BorderBrush="{DynamicResource BorderSubtleBrush}"
                BorderThickness="0,0,0,1">
            <Button Command="{Binding AddConnectionCommand}"
                    HorizontalAlignment="Stretch"
                    HorizontalContentAlignment="Left"
                    Padding="8,5" FontSize="11"
                    Background="{DynamicResource AccentSubtleBrush}"
                    BorderBrush="{DynamicResource AccentBrush}"
                    BorderThickness="1"
                    Foreground="{DynamicResource AccentBrush}"
                    CornerRadius="5">
                ＋ Add Connection
            </Button>
        </Border>

        <!-- Footer -->
        <Border DockPanel.Dock="Bottom" Padding="10,8"
                BorderBrush="{DynamicResource BorderSubtleBrush}"
                BorderThickness="0,1,0,0">
            <TextBlock FontSize="10" Foreground="{DynamicResource TextFaintBrush}">
                ⚙ Settings · ? Docs
            </TextBlock>
        </Border>

        <!-- Section label -->
        <TextBlock DockPanel.Dock="Top"
                   Margin="12,8,0,2" FontSize="9" FontWeight="Bold"
                   Text="CONNECTIONS"
                   Foreground="{DynamicResource TextFaintBrush}" />

        <!-- Connection list -->
        <ScrollViewer>
            <ItemsControl ItemsSource="{Binding Connections}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:ConnectionNodeViewModel}">
                        <Border Padding="4,2">
                            <StackPanel>
                                <!-- Header: dot + name + status badge -->
                                <Grid ColumnDefinitions="Auto,*,Auto" Margin="6,4">
                                    <Ellipse Grid.Column="0"
                                             Width="8" Height="8" Margin="0,0,8,0"
                                             VerticalAlignment="Center"
                                             Fill="{Binding Status, Converter={x:Static vm:ConnectionStatusConverter.Instance}}" />
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding Name}"
                                               FontSize="12" FontWeight="SemiBold"
                                               Foreground="{DynamicResource TextPrimaryBrush}"
                                               VerticalAlignment="Center" />
                                    <Border Grid.Column="2"
                                            CornerRadius="10" Padding="5,1"
                                            Background="{Binding Status, Converter={x:Static vm:ConnectionStatusConverter.Instance}, ConverterParameter=badge}">
                                        <TextBlock Text="{Binding StatusLabel}"
                                                   FontSize="9" FontWeight="Bold"
                                                   Foreground="{Binding Status, Converter={x:Static vm:ConnectionStatusConverter.Instance}}" />
                                    </Border>
                                </Grid>

                                <!-- Action chips: connected -->
                                <StackPanel Orientation="Horizontal" Margin="22,0,0,4" Spacing="2"
                                            IsVisible="{Binding IsConnected}">
                                    <Button Command="{Binding NavigateToTopicsCommand}"
                                            Padding="6,3" FontSize="10"
                                            Background="Transparent" BorderThickness="0"
                                            Foreground="{DynamicResource TextMutedBrush}">
                                        📋 Topics
                                    </Button>
                                    <Button Command="{Binding NavigateToMessagesCommand}"
                                            Padding="6,3" FontSize="10"
                                            Background="Transparent" BorderThickness="0"
                                            Foreground="{DynamicResource TextMutedBrush}">
                                        ✉ Msgs
                                    </Button>
                                    <Button Command="{Binding NavigateToClusterCommand}"
                                            Padding="6,3" FontSize="10"
                                            Background="Transparent" BorderThickness="0"
                                            Foreground="{DynamicResource TextMutedBrush}">
                                        🖥 Cluster
                                    </Button>
                                </StackPanel>

                                <!-- Action chips: disconnected -->
                                <StackPanel Orientation="Horizontal" Margin="22,0,0,4" Spacing="2"
                                            IsVisible="{Binding IsDisconnected}">
                                    <Button Command="{Binding ConnectCommand}"
                                            Padding="6,3" FontSize="10"
                                            Background="Transparent" BorderThickness="0"
                                            Foreground="{DynamicResource TextMutedBrush}">
                                        ⚡ Connect
                                    </Button>
                                    <Button Command="{Binding DeleteCommand}"
                                            Padding="6,3" FontSize="10"
                                            Background="Transparent" BorderThickness="0"
                                            Foreground="{DynamicResource DestructiveTextBrush}">
                                        🗑 Remove
                                    </Button>
                                </StackPanel>

                                <!-- Error message -->
                                <TextBlock Text="{Binding ErrorMessage}"
                                           Foreground="{DynamicResource StatusErrorBrush}"
                                           FontSize="10" TextWrapping="Wrap"
                                           Margin="22,0,6,4"
                                           IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

- [ ] **Step 5: Verify build**

```bash
dotnet build src/Skat.KawkaProject.sln -c Release --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Skat.KawkaProject.UI/ViewModels/ConnectionNodeViewModel.cs \
        src/Skat.KawkaProject.UI/ViewModels/ConnectionStatusConverter.cs \
        src/Skat.KawkaProject.UI/Views/SidebarView.axaml
git commit -m "feat(ui): modernize sidebar with status badges and inline action chips"
```

---

## Task 4: TopicsView Redesign

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`
- Modify: `src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml`

**Interfaces:**
- Consumes: `DynamicResource BackgroundBrush`, `SurfaceBrush`, `SurfaceDeepBrush`, `BorderBrush`, `BorderSubtleBrush`, `TextPrimaryBrush`, `TextMutedBrush`, `AccentBrush`, `AccentSubtleBrush`, `StatusLiveBrush`, `StatusLiveBadgeBrush`, `StatusErrorBrush`, `DestructiveBrush`, `DestructiveTextBrush` (Task 1)
- Produces: `TopicsViewModel.StatusText` (string) — consumed by status bar binding

- [ ] **Step 1: Add StatusText to TopicsViewModel**

In `src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs`:

Add the property after `SelectedTopicDetail`:
```csharp
public string StatusText => string.IsNullOrWhiteSpace(_filter)
    ? $"{_session.ProfileName}  ·  {_allTopics.Count} topics"
    : $"{_session.ProfileName}  ·  {Topics.Count} / {_allTopics.Count} topics";
```

Update `ApplyFilter()` to raise it (add the last line):
```csharp
private void ApplyFilter()
{
    Topics.Clear();
    var filtered = string.IsNullOrWhiteSpace(_filter)
        ? _allTopics
        : _allTopics.Where(t => t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
    foreach (var t in filtered) Topics.Add(t);
    this.RaisePropertyChanged(nameof(StatusText));
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test src/Skat.KawkaProject.sln --no-restore
```

Expected: All 14 tests pass.

- [ ] **Step 3: Rewrite TopicsView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Skat.KawkaProject.Features.Topics.ViewModels"
             x:Class="Skat.KawkaProject.Features.Topics.Views.TopicsView"
             x:DataType="vm:TopicsViewModel"
             Background="{DynamicResource BackgroundBrush}">
    <DockPanel>
        <!-- Toolbar -->
        <Border DockPanel.Dock="Top" Height="34"
                Background="{DynamicResource SurfaceBrush}"
                BorderBrush="{DynamicResource BorderSubtleBrush}"
                BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal" Spacing="8" Margin="10,0" VerticalAlignment="Center">
                <TextBlock FontSize="11" Foreground="{DynamicResource TextMutedBrush}"
                           VerticalAlignment="Center">📋 Topics</TextBlock>
                <Border Width="1" Height="16" Background="{DynamicResource BorderBrush}" />
                <TextBox Text="{Binding Filter}" Watermark="Filter topics…"
                         Width="180" FontSize="11" Height="26" VerticalContentAlignment="Center" />
                <Button Command="{Binding LoadCommand}" FontSize="11" Padding="8,4"
                        Background="{DynamicResource SurfaceBrush}"
                        BorderBrush="{DynamicResource BorderBrush}">↺ Refresh</Button>
            </StackPanel>
        </Border>

        <!-- Error bar -->
        <Border DockPanel.Dock="Top" Padding="10,4"
                Background="{DynamicResource DestructiveBrush}"
                BorderBrush="{DynamicResource StatusErrorBrush}" BorderThickness="0,0,0,1"
                IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Text="{Binding ErrorMessage}"
                           Foreground="{DynamicResource StatusErrorBrush}"
                           FontSize="11" TextWrapping="Wrap" />
                <Button Grid.Column="1" Command="{Binding DismissErrorCommand}"
                        Padding="4,0" Background="Transparent" BorderThickness="0"
                        Foreground="{DynamicResource TextMutedBrush}">✕</Button>
            </Grid>
        </Border>

        <!-- Progress bar -->
        <ProgressBar DockPanel.Dock="Top" IsIndeterminate="True"
                     IsVisible="{Binding IsBusy}" Height="3" />

        <!-- Status bar -->
        <Border DockPanel.Dock="Bottom" Height="22"
                Background="{DynamicResource AccentBrush}">
            <TextBlock Text="{Binding StatusText}"
                       Foreground="#E5FFFFFF" FontSize="10"
                       Margin="10,0" VerticalAlignment="Center" />
        </Border>

        <!-- Column headers -->
        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,80,80,90" Height="28"
              Background="{DynamicResource SurfaceDeepBrush}">
            <TextBlock Grid.Column="0" Text="TOPIC" Margin="12,0"
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
            <TextBlock Grid.Column="1" Text="PARTS"
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
            <TextBlock Grid.Column="2" Text="REPL."
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
            <TextBlock Grid.Column="3" Text="STATUS"
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
        </Grid>

        <!-- Content: topic list + detail panel -->
        <Grid ColumnDefinitions="*,260">
            <ListBox Grid.Column="0" ItemsSource="{Binding Topics}"
                     SelectedItem="{Binding SelectedTopic}"
                     Background="{DynamicResource BackgroundBrush}"
                     BorderThickness="0" Padding="0">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="*,80,80,90" Height="28" VerticalAlignment="Center">
                            <TextBlock Grid.Column="0" Text="{Binding Name}"
                                       Margin="12,0" FontSize="11" FontWeight="SemiBold"
                                       FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                       Foreground="{DynamicResource AccentBrush}"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="1" Text="{Binding PartitionCount}"
                                       FontSize="11" FontWeight="SemiBold"
                                       Foreground="{DynamicResource AccentBrush}"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="2" Text="{Binding ReplicationFactor}"
                                       FontSize="11"
                                       Foreground="{DynamicResource TextMutedBrush}"
                                       VerticalAlignment="Center" />
                            <Border Grid.Column="3" CornerRadius="10"
                                    Background="{DynamicResource StatusLiveBadgeBrush}"
                                    Padding="6,2" Margin="0,4" VerticalAlignment="Center"
                                    HorizontalAlignment="Left">
                                <TextBlock Text="✓ healthy" FontSize="9" FontWeight="Bold"
                                           Foreground="{DynamicResource StatusLiveBrush}" />
                            </Border>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Detail panel -->
            <Border Grid.Column="1"
                    Background="{DynamicResource SurfaceBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="1,0,0,0"
                    IsVisible="{Binding SelectedTopicDetail, Converter={x:Static ObjectConverters.IsNotNull}}">
                <DockPanel>
                    <!-- Detail header -->
                    <Border DockPanel.Dock="Top" Padding="12,8"
                            BorderBrush="{DynamicResource BorderSubtleBrush}"
                            BorderThickness="0,0,0,1">
                        <TextBlock Text="{Binding SelectedTopicDetail.Topic.Name}"
                                   FontSize="11" FontWeight="Bold"
                                   Foreground="{DynamicResource TextPrimaryBrush}" />
                    </Border>

                    <!-- Action buttons -->
                    <Border DockPanel.Dock="Bottom" Padding="8"
                            BorderBrush="{DynamicResource BorderSubtleBrush}"
                            BorderThickness="0,1,0,0">
                        <StackPanel Orientation="Horizontal" Spacing="6">
                            <Button Padding="8,4" FontSize="11"
                                    Background="{DynamicResource SurfaceBrush}"
                                    BorderBrush="{DynamicResource BorderBrush}"
                                    Foreground="{DynamicResource TextPrimaryBrush}">
                                ✉ Browse
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

                    <!-- Partition list -->
                    <ScrollViewer>
                        <StackPanel Margin="12,8">
                            <TextBlock Text="PARTITIONS" FontSize="9" FontWeight="Bold"
                                       Foreground="{DynamicResource TextFaintBrush}"
                                       Margin="0,0,0,6" />
                            <ItemsControl ItemsSource="{Binding SelectedTopicDetail.Partitions}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid ColumnDefinitions="30,*,*" Margin="0,3">
                                            <TextBlock Grid.Column="0" Text="{Binding PartitionId}"
                                                       FontSize="11"
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
                        </StackPanel>
                    </ScrollViewer>
                </DockPanel>
            </Border>
        </Grid>
    </DockPanel>
</UserControl>
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/Skat.KawkaProject.sln -c Release --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Skat.KawkaProject.Features.Topics/ViewModels/TopicsViewModel.cs \
        src/Skat.KawkaProject.Features.Topics/Views/TopicsView.axaml
git commit -m "feat(ui): redesign TopicsView with column headers, compact rows, and status bar"
```

---

## Task 5: MessagesView Redesign

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Messages/ViewModels/MessagesViewModel.cs`
- Modify: `src/Skat.KawkaProject.Features.Messages/Views/MessagesView.axaml`

**Interfaces:**
- Consumes: same DynamicResource tokens as Task 4
- Produces: `MessagesViewModel.StatusText` (string)

- [ ] **Step 1: Add StatusText to MessagesViewModel**

Add field reference (already available: `_session`). Add property after `FilteredMessages`:
```csharp
public string StatusText =>
    $"{_session.ProfileName}  ·  {Messages.Count} messages" +
    (string.IsNullOrWhiteSpace(_clientFilter) ? "" : " (filtered)");
```

In the constructor, after initializing commands, subscribe to collection changes:
```csharp
Messages.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(StatusText));
```

Also raise in `ClientFilter` setter — add `this.RaisePropertyChanged(nameof(StatusText));` after the existing `this.RaisePropertyChanged(nameof(FilteredMessages));`:
```csharp
public string ClientFilter
{
    get => _clientFilter;
    set
    {
        this.RaiseAndSetIfChanged(ref _clientFilter, value);
        this.RaisePropertyChanged(nameof(FilteredMessages));
        this.RaisePropertyChanged(nameof(StatusText));
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test src/Skat.KawkaProject.sln --no-restore
```

Expected: All 14 tests pass.

- [ ] **Step 3: Rewrite MessagesView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Skat.KawkaProject.Features.Messages.ViewModels"
             x:Class="Skat.KawkaProject.Features.Messages.Views.MessagesView"
             x:DataType="vm:MessagesViewModel"
             Background="{DynamicResource BackgroundBrush}">
    <DockPanel>
        <!-- Toolbar -->
        <Border DockPanel.Dock="Top"
                Background="{DynamicResource SurfaceBrush}"
                BorderBrush="{DynamicResource BorderSubtleBrush}"
                BorderThickness="0,0,0,1"
                Padding="8,4">
            <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                <TextBlock FontSize="11" Foreground="{DynamicResource TextMutedBrush}"
                           VerticalAlignment="Center">✉ Messages</TextBlock>
                <Border Width="1" Height="16" Background="{DynamicResource BorderBrush}" />
                <TextBox Text="{Binding TopicName}" Watermark="Topic name"
                         Width="180" FontSize="11" Height="26" VerticalContentAlignment="Center" />
                <ComboBox ItemsSource="{Binding Modes}" SelectedItem="{Binding Mode}"
                          Height="26" FontSize="11" />
                <StackPanel Orientation="Horizontal" Spacing="4" IsVisible="{Binding IsOffsetMode}">
                    <TextBlock VerticalAlignment="Center" FontSize="11"
                               Foreground="{DynamicResource TextMutedBrush}">Partition:</TextBlock>
                    <NumericUpDown Value="{Binding Partition}" Minimum="0"
                                   Width="70" Height="26" FontSize="11" />
                    <TextBlock VerticalAlignment="Center" FontSize="11"
                               Foreground="{DynamicResource TextMutedBrush}">From:</TextBlock>
                    <NumericUpDown Value="{Binding StartOffset}" Minimum="0"
                                   Width="90" Height="26" FontSize="11" />
                    <TextBlock VerticalAlignment="Center" FontSize="11"
                               Foreground="{DynamicResource TextMutedBrush}">Count:</TextBlock>
                    <NumericUpDown Value="{Binding FetchCount}" Minimum="1" Maximum="1000"
                                   Width="70" Height="26" FontSize="11" />
                    <Button Command="{Binding FetchCommand}" Padding="8,4" FontSize="11"
                            Background="{DynamicResource AccentSubtleBrush}"
                            BorderBrush="{DynamicResource AccentBrush}"
                            Foreground="{DynamicResource AccentBrush}">Fetch</Button>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="4" IsVisible="{Binding IsTailMode}">
                    <Button Command="{Binding StartTailCommand}" Padding="8,4" FontSize="11"
                            Background="{DynamicResource AccentSubtleBrush}"
                            BorderBrush="{DynamicResource AccentBrush}"
                            Foreground="{DynamicResource AccentBrush}">▶ Tail</Button>
                    <Button Command="{Binding PauseCommand}" Padding="8,4" FontSize="11">⏸ Pause</Button>
                    <Button Command="{Binding StopTailCommand}" Padding="8,4" FontSize="11">⏹ Stop</Button>
                </StackPanel>
                <TextBox Text="{Binding ClientFilter}" Watermark="Filter loaded…"
                         Width="160" FontSize="11" Height="26" VerticalContentAlignment="Center" />
            </StackPanel>
        </Border>

        <!-- Error bar -->
        <Border DockPanel.Dock="Top" Padding="10,4"
                Background="{DynamicResource DestructiveBrush}"
                BorderBrush="{DynamicResource StatusErrorBrush}" BorderThickness="0,0,0,1"
                IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Text="{Binding ErrorMessage}"
                           Foreground="{DynamicResource StatusErrorBrush}"
                           FontSize="11" TextWrapping="Wrap" />
                <Button Grid.Column="1" Command="{Binding DismissErrorCommand}"
                        Padding="4,0" Background="Transparent" BorderThickness="0"
                        Foreground="{DynamicResource TextMutedBrush}">✕</Button>
            </Grid>
        </Border>

        <!-- Progress bar -->
        <ProgressBar DockPanel.Dock="Top" IsIndeterminate="True"
                     IsVisible="{Binding IsBusy}" Height="3" />

        <!-- Status bar -->
        <Border DockPanel.Dock="Bottom" Height="22"
                Background="{DynamicResource AccentBrush}">
            <TextBlock Text="{Binding StatusText}"
                       Foreground="#E5FFFFFF" FontSize="10"
                       Margin="10,0" VerticalAlignment="Center" />
        </Border>

        <!-- Column headers -->
        <Grid DockPanel.Dock="Top" ColumnDefinitions="60,70,150,*" Height="28"
              Background="{DynamicResource SurfaceDeepBrush}">
            <TextBlock Grid.Column="0" Text="PART." Margin="8,0"
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
            <TextBlock Grid.Column="1" Text="OFFSET"
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
            <TextBlock Grid.Column="2" Text="TIMESTAMP"
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
            <TextBlock Grid.Column="3" Text="VALUE"
                       FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
        </Grid>

        <!-- Messages list + detail pane -->
        <Grid RowDefinitions="*,180">
            <ListBox Grid.Row="0" ItemsSource="{Binding FilteredMessages}"
                     SelectedItem="{Binding SelectedMessage}"
                     Background="{DynamicResource BackgroundBrush}"
                     BorderThickness="0" Padding="0">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="60,70,150,*" Height="28" VerticalAlignment="Center">
                            <TextBlock Grid.Column="0" Text="{Binding Partition}"
                                       Margin="8,0" FontSize="11" FontWeight="SemiBold"
                                       Foreground="{DynamicResource AccentBrush}"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="1" Text="{Binding Offset}"
                                       FontSize="11"
                                       FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                       Foreground="{DynamicResource AccentBrush}"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="2"
                                       Text="{Binding Timestamp, StringFormat='{}{0:HH:mm:ss.fff}'}"
                                       FontSize="11"
                                       Foreground="{DynamicResource TextMutedBrush}"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="3" Text="{Binding Value}"
                                       FontSize="11"
                                       Foreground="{DynamicResource TextPrimaryBrush}"
                                       TextTrimming="CharacterEllipsis"
                                       VerticalAlignment="Center" />
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Message detail pane -->
            <Border Grid.Row="1" Padding="12,8"
                    Background="{DynamicResource SurfaceBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="0,1,0,0">
                <ScrollViewer>
                    <TextBlock Text="{Binding SelectedMessageValue}"
                               TextWrapping="Wrap"
                               FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                               FontSize="11"
                               Foreground="{DynamicResource TextPrimaryBrush}" />
                </ScrollViewer>
            </Border>
        </Grid>
    </DockPanel>
</UserControl>
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/Skat.KawkaProject.sln -c Release --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Skat.KawkaProject.Features.Messages/ViewModels/MessagesViewModel.cs \
        src/Skat.KawkaProject.Features.Messages/Views/MessagesView.axaml
git commit -m "feat(ui): redesign MessagesView with column headers and status bar"
```

---

## Task 6: ClusterView Redesign

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Cluster/ViewModels/ClusterViewModel.cs`
- Modify: `src/Skat.KawkaProject.Features.Cluster/Views/ClusterView.axaml`

**Interfaces:**
- Produces: `ClusterViewModel.StatusText` (string: `"<profile> · N brokers · M groups"`)

- [ ] **Step 1: Add StatusText to ClusterViewModel**

Add property after `SelectedGroup`:
```csharp
public string StatusText =>
    $"{_session.ProfileName}  ·  {Brokers.Count} brokers  ·  {ConsumerGroups.Count} groups";
```

In the constructor (after `LoadCommand` etc. assignments), subscribe:
```csharp
Brokers.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(StatusText));
ConsumerGroups.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(StatusText));
```

- [ ] **Step 2: Run tests**

```bash
dotnet test src/Skat.KawkaProject.sln --no-restore
```

Expected: All 14 tests pass.

- [ ] **Step 3: Rewrite ClusterView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Skat.KawkaProject.Features.Cluster.ViewModels"
             x:Class="Skat.KawkaProject.Features.Cluster.Views.ClusterView"
             x:DataType="vm:ClusterViewModel"
             Background="{DynamicResource BackgroundBrush}">
    <DockPanel>
        <!-- Toolbar -->
        <Border DockPanel.Dock="Top" Height="34"
                Background="{DynamicResource SurfaceBrush}"
                BorderBrush="{DynamicResource BorderSubtleBrush}"
                BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal" Spacing="8" Margin="10,0" VerticalAlignment="Center">
                <TextBlock FontSize="11" Foreground="{DynamicResource TextMutedBrush}"
                           VerticalAlignment="Center">🖥 Cluster</TextBlock>
                <Border Width="1" Height="16" Background="{DynamicResource BorderBrush}" />
                <Button Command="{Binding LoadCommand}" FontSize="11" Padding="8,4"
                        Background="{DynamicResource SurfaceBrush}"
                        BorderBrush="{DynamicResource BorderBrush}">↺ Refresh</Button>
            </StackPanel>
        </Border>

        <!-- Error bar -->
        <Border DockPanel.Dock="Top" Padding="10,4"
                Background="{DynamicResource DestructiveBrush}"
                BorderBrush="{DynamicResource StatusErrorBrush}" BorderThickness="0,0,0,1"
                IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <TextBlock Text="{Binding ErrorMessage}"
                       Foreground="{DynamicResource StatusErrorBrush}"
                       FontSize="11" TextWrapping="Wrap" />
        </Border>

        <!-- Progress bar -->
        <ProgressBar DockPanel.Dock="Top" IsIndeterminate="True"
                     IsVisible="{Binding IsBusy}" Height="3" />

        <!-- Status bar -->
        <Border DockPanel.Dock="Bottom" Height="22"
                Background="{DynamicResource AccentBrush}">
            <TextBlock Text="{Binding StatusText}"
                       Foreground="#E5FFFFFF" FontSize="10"
                       Margin="10,0" VerticalAlignment="Center" />
        </Border>

        <!-- TabControl -->
        <TabControl Background="{DynamicResource BackgroundBrush}">

            <!-- Brokers tab -->
            <TabItem Header="Brokers">
                <DockPanel>
                    <Grid DockPanel.Dock="Top" ColumnDefinitions="60,*,80,80" Height="28"
                          Background="{DynamicResource SurfaceDeepBrush}">
                        <TextBlock Grid.Column="0" Text="ID" Margin="8,0"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="1" Text="HOST"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="2" Text="PORT"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="3" Text="CTRL"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                    </Grid>
                    <ListBox ItemsSource="{Binding Brokers}"
                             Background="{DynamicResource BackgroundBrush}"
                             BorderThickness="0" Padding="0">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="60,*,80,80" Height="28" VerticalAlignment="Center">
                                    <TextBlock Grid.Column="0" Text="{Binding BrokerId}"
                                               Margin="8,0" FontSize="11" FontWeight="SemiBold"
                                               Foreground="{DynamicResource AccentBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="1" Text="{Binding Host}"
                                               FontSize="11"
                                               FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                               Foreground="{DynamicResource TextPrimaryBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="2" Text="{Binding Port}"
                                               FontSize="11"
                                               Foreground="{DynamicResource TextMutedBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="3" Text="{Binding IsController}"
                                               FontSize="11"
                                               Foreground="{DynamicResource TextMutedBrush}"
                                               VerticalAlignment="Center" />
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
            </TabItem>

            <!-- Consumer Groups tab -->
            <TabItem Header="Consumer Groups">
                <DockPanel>
                    <Grid DockPanel.Dock="Top" ColumnDefinitions="*,100,60" Height="28"
                          Background="{DynamicResource SurfaceDeepBrush}">
                        <TextBlock Grid.Column="0" Text="GROUP ID" Margin="8,0"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="1" Text="STATE"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="2" Text="MEMBERS"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                    </Grid>
                    <ListBox ItemsSource="{Binding ConsumerGroups}" SelectedItem="{Binding SelectedGroup}"
                             Background="{DynamicResource BackgroundBrush}"
                             BorderThickness="0" Padding="0">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,100,60" Height="28" VerticalAlignment="Center">
                                    <TextBlock Grid.Column="0" Text="{Binding GroupId}"
                                               Margin="8,0" FontSize="11"
                                               FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                               Foreground="{DynamicResource AccentBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="1" Text="{Binding State}"
                                               FontSize="11"
                                               Foreground="{DynamicResource TextMutedBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="2" Text="{Binding MemberCount}"
                                               FontSize="11" FontWeight="SemiBold"
                                               Foreground="{DynamicResource TextPrimaryBrush}"
                                               VerticalAlignment="Center" />
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
            </TabItem>

            <!-- Lag tab -->
            <TabItem Header="Lag">
                <DockPanel>
                    <Border DockPanel.Dock="Top"
                            Background="{DynamicResource SurfaceBrush}"
                            BorderBrush="{DynamicResource BorderSubtleBrush}"
                            BorderThickness="0,0,0,1"
                            Padding="8,4">
                        <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                            <ComboBox ItemsSource="{Binding ConsumerGroups}"
                                      SelectedItem="{Binding SelectedGroup}"
                                      DisplayMemberBinding="{Binding GroupId}"
                                      Width="200" FontSize="11" />
                            <Button Command="{Binding LoadLagCommand}" FontSize="11" Padding="8,4"
                                    Background="{DynamicResource AccentSubtleBrush}"
                                    BorderBrush="{DynamicResource AccentBrush}"
                                    Foreground="{DynamicResource AccentBrush}">Load Lag</Button>
                            <TextBlock VerticalAlignment="Center" FontSize="10"
                                       Foreground="{DynamicResource TextFaintBrush}">Auto-refreshes every 10 s</TextBlock>
                        </StackPanel>
                    </Border>
                    <Grid DockPanel.Dock="Top" ColumnDefinitions="*,60,100,100,80" Height="28"
                          Background="{DynamicResource SurfaceDeepBrush}">
                        <TextBlock Grid.Column="0" Text="TOPIC" Margin="8,0"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="1" Text="PART."
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="2" Text="CURRENT"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="3" Text="END"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="4" Text="LAG"
                                   FontSize="10" FontWeight="Bold"
                                   Foreground="{DynamicResource TextMutedBrush}" VerticalAlignment="Center" />
                    </Grid>
                    <ListBox ItemsSource="{Binding Lag}"
                             Background="{DynamicResource BackgroundBrush}"
                             BorderThickness="0" Padding="0">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,60,100,100,80" Height="28" VerticalAlignment="Center">
                                    <TextBlock Grid.Column="0" Text="{Binding Topic}"
                                               Margin="8,0" FontSize="11"
                                               FontFamily="Cascadia Code,JetBrains Mono,Consolas,monospace"
                                               Foreground="{DynamicResource AccentBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="1" Text="{Binding Partition}"
                                               FontSize="11"
                                               Foreground="{DynamicResource TextMutedBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="2" Text="{Binding CurrentOffset}"
                                               FontSize="11"
                                               Foreground="{DynamicResource TextPrimaryBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="3" Text="{Binding EndOffset}"
                                               FontSize="11"
                                               Foreground="{DynamicResource TextPrimaryBrush}"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="4" Text="{Binding Lag}"
                                               FontSize="11" FontWeight="Bold"
                                               Foreground="{DynamicResource StatusConnBrush}"
                                               VerticalAlignment="Center" />
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

- [ ] **Step 4: Verify build**

```bash
dotnet build src/Skat.KawkaProject.sln -c Release --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Skat.KawkaProject.Features.Cluster/ViewModels/ClusterViewModel.cs \
        src/Skat.KawkaProject.Features.Cluster/Views/ClusterView.axaml
git commit -m "feat(ui): redesign ClusterView with column headers, styled tabs, and status bar"
```

---

## Task 7: ConnectionEditorView Styling

**Files:**
- Modify: `src/Skat.KawkaProject.Features.Connections/Views/ConnectionEditorView.axaml`

**Interfaces:**
- No ViewModel changes. Uses existing `ConnectionEditorViewModel` bindings unchanged.

- [ ] **Step 1: Rewrite ConnectionEditorView.axaml**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Skat.KawkaProject.Features.Connections.ViewModels"
        x:Class="Skat.KawkaProject.Features.Connections.Views.ConnectionEditorView"
        x:DataType="vm:ConnectionEditorViewModel"
        Title="Add / Edit Connection"
        Width="480" Height="420"
        CanResize="False"
        Background="{DynamicResource SurfaceBrush}"
        FontFamily="Segoe UI,system-ui,-apple-system,sans-serif">
    <StackPanel Margin="20" Spacing="10">
        <!-- Name -->
        <TextBlock Text="Name" FontSize="10" FontWeight="Bold"
                   Foreground="{DynamicResource TextMutedBrush}" />
        <TextBox Text="{Binding Name}" Height="30" FontSize="12"
                 Background="{DynamicResource SurfaceDeepBrush}"
                 BorderBrush="{DynamicResource BorderBrush}"
                 Foreground="{DynamicResource TextPrimaryBrush}" />

        <!-- Bootstrap Servers -->
        <TextBlock Text="Bootstrap Servers (e.g. localhost:9092)"
                   FontSize="10" FontWeight="Bold"
                   Foreground="{DynamicResource TextMutedBrush}" />
        <TextBox Text="{Binding BootstrapServers}" Height="30" FontSize="12"
                 Background="{DynamicResource SurfaceDeepBrush}"
                 BorderBrush="{DynamicResource BorderBrush}"
                 Foreground="{DynamicResource TextPrimaryBrush}" />

        <!-- Authentication -->
        <TextBlock Text="Authentication" FontSize="10" FontWeight="Bold"
                   Foreground="{DynamicResource TextMutedBrush}" />
        <ComboBox ItemsSource="{Binding AuthTypes}" SelectedItem="{Binding AuthType}"
                  Height="30" FontSize="12" HorizontalAlignment="Stretch"
                  Background="{DynamicResource SurfaceDeepBrush}"
                  BorderBrush="{DynamicResource BorderBrush}"
                  Foreground="{DynamicResource TextPrimaryBrush}" />

        <!-- SASL fields -->
        <StackPanel IsVisible="{Binding ShowSaslFields}" Spacing="6">
            <TextBlock Text="SASL Username" FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" />
            <TextBox Text="{Binding SaslUsername}" Height="30" FontSize="12"
                     Background="{DynamicResource SurfaceDeepBrush}"
                     BorderBrush="{DynamicResource BorderBrush}"
                     Foreground="{DynamicResource TextPrimaryBrush}" />
            <TextBlock Text="SASL Password" FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" />
            <TextBox Text="{Binding SaslPassword}" PasswordChar="*" Height="30" FontSize="12"
                     Background="{DynamicResource SurfaceDeepBrush}"
                     BorderBrush="{DynamicResource BorderBrush}"
                     Foreground="{DynamicResource TextPrimaryBrush}" />
        </StackPanel>

        <!-- SSL fields -->
        <StackPanel IsVisible="{Binding ShowSslFields}" Spacing="6">
            <TextBlock Text="SSL Certificate Path" FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" />
            <TextBox Text="{Binding SslCertPath}" Height="30" FontSize="12"
                     Background="{DynamicResource SurfaceDeepBrush}"
                     BorderBrush="{DynamicResource BorderBrush}"
                     Foreground="{DynamicResource TextPrimaryBrush}" />
            <TextBlock Text="SSL Key Path" FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" />
            <TextBox Text="{Binding SslKeyPath}" Height="30" FontSize="12"
                     Background="{DynamicResource SurfaceDeepBrush}"
                     BorderBrush="{DynamicResource BorderBrush}"
                     Foreground="{DynamicResource TextPrimaryBrush}" />
            <TextBlock Text="SSL CA Path" FontSize="10" FontWeight="Bold"
                       Foreground="{DynamicResource TextMutedBrush}" />
            <TextBox Text="{Binding SslCaPath}" Height="30" FontSize="12"
                     Background="{DynamicResource SurfaceDeepBrush}"
                     BorderBrush="{DynamicResource BorderBrush}"
                     Foreground="{DynamicResource TextPrimaryBrush}" />
        </StackPanel>

        <!-- Action buttons -->
        <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right" Margin="0,6,0,0">
            <Button Command="{Binding CancelCommand}" Padding="12,6" FontSize="12"
                    Background="{DynamicResource SurfaceBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    Foreground="{DynamicResource TextMutedBrush}">Cancel</Button>
            <Button Command="{Binding SaveCommand}" Padding="12,6" FontSize="12"
                    Background="{DynamicResource AccentSubtleBrush}"
                    BorderBrush="{DynamicResource AccentBrush}"
                    Foreground="{DynamicResource AccentBrush}"
                    FontWeight="SemiBold">Save</Button>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/Skat.KawkaProject.sln -c Release --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test src/Skat.KawkaProject.sln --no-restore
```

Expected: All 14 tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Skat.KawkaProject.Features.Connections/Views/ConnectionEditorView.axaml
git commit -m "feat(ui): style ConnectionEditorView with Surface background and primary Save button"
```

---

## Self-Review

**Spec coverage check:**
- ✅ Dark/Light ThemeDictionaries — Task 1
- ✅ `#0088CC` accent in both themes — Task 1 (same hex in both)
- ✅ Runtime theme toggle — Task 2 (`ToggleThemeCommand`)
- ✅ Header bar with theme toggle button — Task 2 (inside Window content)
- ✅ Sidebar: dot + name + badge + action chips — Task 3
- ✅ Status badges (live/conn/off) via `StatusLabel` — Task 3
- ✅ Table column headers at 10 px uppercase, bold — Tasks 4–6
- ✅ Compact rows at 28 px height — Tasks 4–6
- ✅ Status bar at 22 px with Accent background — Tasks 4–6
- ✅ Detail panel in TopicsView (260 px, SurfaceBrush, header + partitions + action buttons) — Task 4
- ✅ Monospace font for topic names, offsets, message values — Tasks 4–6
- ✅ Accent foreground for numeric/key values — Tasks 4–6
- ✅ Progress bar at 3–4 px height — Tasks 4–6
- ✅ Error bar with DestructiveBrush background — Tasks 4–6
- ✅ ConnectionEditorView form styling — Task 7
- ✅ Global control styles (TextBox, Button, ListBox hover/selection) — Task 1
- ✅ Font family cascade: `Segoe UI,system-ui,-apple-system,sans-serif` — Tasks 2, 7

**Known limitation:** The `"✓ healthy"` badge in TopicsView is static for all topics. `TopicInfo` has no `Status` field. Dynamic topic health would require a backend model change — out of scope for this styling plan.

**Placeholder scan:** No TBDs, no "implement later", all code complete.

**Type consistency:** `StatusText` property name is consistent across TopicsViewModel (Task 4), MessagesViewModel (Task 5), ClusterViewModel (Task 6). `ToggleThemeCommand` is consistent between ShellViewModel (Task 2) and MainWindow binding (Task 2).
