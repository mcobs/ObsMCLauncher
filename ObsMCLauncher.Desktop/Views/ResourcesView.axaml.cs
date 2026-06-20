using Avalonia;
using Avalonia.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class ResourcesView : UserControl
{
    private bool _isScrollLoading;

    public ResourcesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnResourceScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer == null) return;

        // 渐变遮罩淡出
        UpdateGradientMaskOpacity(scrollViewer, ResourceGradientMask);

        // 无限滚动加载
        if (_isScrollLoading) return;

        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 100)
        {
            _isScrollLoading = true;
            var vm = DataContext as ResourcesViewModel;
            vm?.LoadMoreResourcesCommand.Execute(null);
            _isScrollLoading = false;
        }
    }

    private void UpdateGradientMaskOpacity(ScrollViewer scrollViewer, Border mask)
    {
        if (scrollViewer.Extent.Height <= scrollViewer.Viewport.Height)
        {
            mask.Opacity = 0;
            return;
        }

        double distanceFromBottom = scrollViewer.Extent.Height - scrollViewer.Offset.Y - scrollViewer.Viewport.Height;
        double fadeRange = 80;

        if (distanceFromBottom <= 0)
            mask.Opacity = 0;
        else if (distanceFromBottom >= fadeRange)
            mask.Opacity = 1;
        else
            mask.Opacity = distanceFromBottom / fadeRange;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ResourceScrollViewer != null && ResourceScrollViewer.Extent.Height <= ResourceScrollViewer.Viewport.Height)
            ResourceGradientMask.Opacity = 0;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ResourcesViewModel vm)
        {
            vm.IsViewReady = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is ResourcesViewModel vm)
        {
            vm.IsViewReady = false;
        }
    }
}
