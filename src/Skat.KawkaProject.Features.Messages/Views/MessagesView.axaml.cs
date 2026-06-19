using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Skat.KawkaProject.Features.Messages.ViewModels;

namespace Skat.KawkaProject.Features.Messages.Views;

public partial class MessagesView : ReactiveUserControl<MessagesViewModel>
{
    public MessagesView()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            ViewModel!.ConfirmFetchAll.RegisterHandler(async ctx =>
            {
                ctx.SetOutput(await ShowConfirmDialogAsync());
            }).DisposeWith(d);
        });
    }

    private async Task<bool> ShowConfirmDialogAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        var message = new TextBlock
        {
            Text = "No topic specified. Messages from all available topics will be read, which may take a few minutes.\n\nDo you want to continue?",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var confirmBtn = new Button
        {
            Content = "Yes, continue",
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
            Spacing = 0,
            Children = { cancelBtn, confirmBtn }
        };

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Children = { message, buttons }
        };

        var dialog = new Window
        {
            Title = "Confirm operation",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Content = content
        };

        confirmBtn.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
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
