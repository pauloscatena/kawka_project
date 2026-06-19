using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Skat.KawkaProject.Features.Topics.ViewModels;

namespace Skat.KawkaProject.Features.Topics.Views;

public partial class TopicsView : ReactiveUserControl<TopicsViewModel>
{
    public TopicsView()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            ViewModel!.ConfirmDelete.RegisterHandler(async ctx =>
            {
                ctx.SetOutput(await ShowConfirmDeleteAsync(ctx.Input));
            }).DisposeWith(d);
        });
    }

    private async Task<bool> ShowConfirmDeleteAsync(string topicName)
    {
        var tcs = new TaskCompletionSource<bool>();

        var message = new TextBlock
        {
            Text = $"Delete topic \"{topicName}\"?\n\nAll messages in this topic will be permanently deleted and cannot be recovered.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var deleteBtn = new Button
        {
            Content = "Delete topic",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 6)
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelBtn, deleteBtn }
        };

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Children = { message, buttons }
        };

        var dialog = new Window
        {
            Title = "Confirm deletion",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Content = content
        };

        deleteBtn.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return await tcs.Task;
    }
}
