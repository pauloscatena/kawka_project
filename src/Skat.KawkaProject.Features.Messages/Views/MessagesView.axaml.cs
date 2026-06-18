using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Messages.ViewModels;

namespace Skat.KawkaProject.Features.Messages.Views;

public partial class MessagesView : ReactiveUserControl<MessagesViewModel>
{
    public MessagesView() => InitializeComponent();
}
