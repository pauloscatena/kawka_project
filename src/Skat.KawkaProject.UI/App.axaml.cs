using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Features.Cluster.ViewModels;
using Skat.KawkaProject.Features.Cluster.Views;
using Skat.KawkaProject.Features.Messages.ViewModels;
using Skat.KawkaProject.Features.Messages.Views;
using Skat.KawkaProject.Features.Topics.ViewModels;
using Skat.KawkaProject.Features.Topics.Views;
using Skat.KawkaProject.Kafka;
using Skat.KawkaProject.UI.ViewModels;
using Skat.KawkaProject.UI.Views;
using Splat;

namespace Skat.KawkaProject.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Locator.CurrentMutable.Register<IViewFor<TopicsViewModel>>(() => new TopicsView());
        Locator.CurrentMutable.Register<IViewFor<MessagesViewModel>>(() => new MessagesView());
        Locator.CurrentMutable.Register<IViewFor<ClusterViewModel>>(() => new ClusterView());

        var collection = new ServiceCollection();
        collection.AddSingleton<IConnectionProfileRepository, ConnectionProfileRepository>();
        collection.AddSingleton<IKafkaConnectionFactory, KafkaConnectionFactory>();
        collection.AddTransient<ITopicService, TopicService>();
        collection.AddTransient<IMessageService, MessageService>();
        collection.AddTransient<IClusterService, ClusterService>();
        collection.AddSingleton<ShellViewModel>();
        Services = collection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<ShellViewModel>()
            };

        base.OnFrameworkInitializationCompleted();
    }
}
