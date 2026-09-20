using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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
        // ⚠️ 事件挂在 ListBox 上时，sender 是 ListBox，真正的 ScrollViewer 在 e.Source。
        // 旧写法 `sender as ScrollViewer` 恒为 null → 这里一直静默 return，滚动到底从不加载更多。
        var scrollViewer = (e.Source as ScrollViewer) ?? sender as ScrollViewer;
        if (scrollViewer == null || _isScreenshotsScrollLoading) return;

        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height < scrollViewer.Extent.Height - 100) return;

        // 追加数据会改变 extent，而 ScrollChanged 是在 arrange 期间触发的：
        // 直接在这里改集合等于"在布局里再标脏一次" → 列表被反复重排（[Layout] Layout cycle detected）。
        // 因此推迟到布局之后的 Background 优先级执行，并让守卫覆盖整个异步窗口。
        _isScreenshotsScrollLoading = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    (scrollViewer.DataContext as ScreenshotsViewModel)?.LoadMoreCommand.Execute(null);
                }
                finally
                {
                    _isScreenshotsScrollLoading = false;
                }
            },
            DispatcherPriority.Background);
    }

    private void TitleText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MoreViewModel moreVm && moreVm.About is AboutViewModel aboutVm)
        {
            aboutVm.OnTitleClick();
        }
    }
}
