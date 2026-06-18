using Avalonia.ReactiveUI;
using Skat.KawkaProject.Features.Cluster.ViewModels;

namespace Skat.KawkaProject.Features.Cluster.Views;

public partial class ClusterView : ReactiveUserControl<ClusterViewModel>
{
    public ClusterView() => InitializeComponent();
}
