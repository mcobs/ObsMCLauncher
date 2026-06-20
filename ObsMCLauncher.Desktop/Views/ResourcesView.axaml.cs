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
    }

    private void OnResourceScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer == null || _isScrollLoading) return;

        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 100)
        {
            _isScrollLoading = true;
            var vm = DataContext as ResourcesViewModel;
            vm?.LoadMoreResourcesCommand.Execute(null);
            _isScrollLoading = false;
        }
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
