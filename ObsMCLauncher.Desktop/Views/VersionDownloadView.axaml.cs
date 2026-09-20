using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ObsMCLauncher.Desktop.Views;

public partial class VersionDownloadView : UserControl
{
    private TranslateTransform? _sidebarTransform;
    private bool _isAnimating;
    private bool _isScrollLoading;

    public VersionDownloadView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnOnlineVersionScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // ⚠️ 事件挂在 ListBox 上时，sender 是 ListBox，真正的 ScrollViewer 在 e.Source。
        // 旧写法 `sender as ScrollViewer` 恒为 null → 这里一直静默 return，滚动到底从不加载更多。
        var scrollViewer = (e.Source as ScrollViewer) ?? sender as ScrollViewer;
        if (scrollViewer == null || _isScrollLoading) return;

        // 距底部不到 100px 时触发加载
        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height < scrollViewer.Extent.Height - 100) return;

        // 追加数据会改变 extent，而 ScrollChanged 是在 arrange 期间触发的：
        // 直接在这里改集合等于"在布局里再标脏一次" → 列表被反复重排（[Layout] Layout cycle detected）。
        // 因此推迟到布局之后的 Background 优先级执行，并让守卫覆盖整个异步窗口。
        _isScrollLoading = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    var vm = DataContext as ViewModels.VersionDownloadViewModel;
                    vm?.LoadMoreVersionsCommand.Execute(null);
                }
                finally
                {
                    _isScrollLoading = false;
                }
            },
            DispatcherPriority.Background);
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _sidebarTransform = SidebarPanel.RenderTransform as TranslateTransform;
        if (_sidebarTransform == null)
        {
            _sidebarTransform = new TranslateTransform(-300, 0);
            SidebarPanel.RenderTransform = _sidebarTransform;
        }

        var vm = DataContext as ViewModels.VersionDownloadViewModel;
        if (vm != null)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        SidebarOverlay.PointerPressed += (_, _) =>
        {
            if (vm != null && vm.IsSidebarOpen)
            {
                vm.ToggleSidebarCommand.Execute(null);
            }
        };
    }

    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModels.VersionDownloadViewModel.IsSidebarOpen) || _sidebarTransform == null)
            return;

        var vm = sender as ViewModels.VersionDownloadViewModel;
        if (vm == null) return;

        if (_isAnimating) return;
        _isAnimating = true;

        double targetX = vm.IsSidebarOpen ? 0 : -300;
        double currentX = _sidebarTransform.X;

        await AnimateSidebarAsync(currentX, targetX);

        _isAnimating = false;
    }

    private async Task AnimateSidebarAsync(double from, double to)
    {
        int steps = 12;
        double duration = 0.25;
        int stepDelay = (int)((duration / steps) * 1000);

        var easing = new QuadraticEaseOut();

        for (int i = 1; i <= steps; i++)
        {
            double progress = (double)i / steps;
            double easedProgress = easing.Ease(progress);
            double currentX = from + (to - from) * easedProgress;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_sidebarTransform != null)
                {
                    _sidebarTransform.X = currentX;
                }
            });

            await Task.Delay(stepDelay);
        }
    }
}