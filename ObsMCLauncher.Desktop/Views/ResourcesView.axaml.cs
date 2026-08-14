using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>点击结果卡片任意位置打开详情（操作按钮已自行拦截点击）</summary>
    private void OnResourceRowTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Button) return;

        if (sender is Control { DataContext: ResourceItemViewModel item } &&
            DataContext is ResourcesViewModel vm)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }

    /// <summary>拦截操作按钮的点击冒泡，避免同时触发行点击</summary>
    private void OnActionTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
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
