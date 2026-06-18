using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Connections.ViewModels;

namespace Skat.KawkaProject.Features.Connections.Views;

public partial class ConnectionEditorView : ReactiveWindow<ConnectionEditorViewModel>
{
    public ConnectionEditorView() => InitializeComponent();
}
