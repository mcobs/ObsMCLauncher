using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.Views;

public partial class MoreView : UserControl
{
    private bool _isScreenshotsScrollLoading;

    public MoreView()
    {
        InitializeComponent();
    }

    private void OnScreenshotsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer == null || _isScreenshotsScrollLoading) return;

        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 100)
        {
            _isScreenshotsScrollLoading = true;
            (scrollViewer.DataContext as ScreenshotsViewModel)?.LoadMoreCommand.Execute(null);
            _isScreenshotsScrollLoading = false;
        }
    }

    private void TitleText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MoreViewModel moreVm && moreVm.About is AboutViewModel aboutVm)
        {
            aboutVm.OnTitleClick();
        }
    }
}
