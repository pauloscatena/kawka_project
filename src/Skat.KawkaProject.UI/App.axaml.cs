using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Skat.KawkaProject.UI.ViewModels;
using Skat.KawkaProject.UI.Views;

namespace Skat.KawkaProject.UI
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new SendMessageViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}