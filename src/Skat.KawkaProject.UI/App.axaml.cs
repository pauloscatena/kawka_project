using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Kafka;
using Skat.KawkaProject.UI.ViewModels;
using Skat.KawkaProject.UI.Views;

namespace Skat.KawkaProject.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
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
