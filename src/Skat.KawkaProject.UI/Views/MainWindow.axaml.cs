using Avalonia.ReactiveUI;
using Skat.KawkaProject.UI.ViewModels;

namespace Skat.KawkaProject.UI.Views;

public partial class MainWindow : ReactiveWindow<ShellViewModel>
{
    public MainWindow() => InitializeComponent();
}
