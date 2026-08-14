using System;
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

    /// <summary>
    /// 标题行：让来源标签紧跟资源名，同时按剩余空间动态限制标题宽度（省略号截断），
    /// 保证长标题也不会横向挤压右侧的"详情"按钮。
    /// </summary>
    private void OnTitleRowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not StackPanel panel) return;
        if (panel.Children.Count == 0 || panel.Children[0] is not TextBlock title) return;

        // 计算来源标签占用的宽度（含间距），标题使用剩余空间
        double reserved = 0;
        for (var i = 1; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is Control child && child.IsVisible)
                reserved += child.Bounds.Width + 6;
        }

        title.MaxWidth = Math.Max(60, e.NewSize.Width - reserved - 2);
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
