using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Topics.ViewModels;

namespace Skat.KawkaProject.Features.Topics.Views;

public partial class TopicsView : ReactiveUserControl<TopicsViewModel>
{
    public TopicsView() => InitializeComponent();
}
