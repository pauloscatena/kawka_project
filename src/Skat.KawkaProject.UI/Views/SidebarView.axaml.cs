using Avalonia.ReactiveUI;
using Skat.KawkaProject.UI.ViewModels;

namespace Skat.KawkaProject.UI.Views;

public partial class SidebarView : ReactiveUserControl<SidebarViewModel>
{
    public SidebarView() => InitializeComponent();
}
