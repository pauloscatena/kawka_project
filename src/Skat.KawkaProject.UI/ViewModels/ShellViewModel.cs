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
