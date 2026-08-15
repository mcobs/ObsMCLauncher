using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private bool _navInitialized;
    private bool _shutdownRequested;

    public MainWindow()
    {
        InitializeComponent();

        PropertyChanged += OnWindowPropertyChanged;
        Closing += MainWindow_Closing;
        DataContextChanged += (_, _) => HookVm();
        HookVm();

        if (_vm != null)
        {
            _vm.WindowWidth = Width;
        }

        MainFrame.NavigationPageFactory = new ViewModelPageFactory();
        MainNav.SelectionChanged += OnNavSelectionChanged;
        MainNav.Loaded += OnMainNavLoaded;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WidthProperty && _vm != null)
        {
            _vm.WindowWidth = Width;
        }
    }

    private void HookVm()
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
        }

        _vm = DataContext as MainWindowViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += VmOnPropertyChanged;
            _vm.WindowWidth = Width;
        }

        UpdateNotificationPosition();
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.NotificationPosition):
                Dispatcher.UIThread.Post(UpdateNotificationPosition);
                break;

            case nameof(MainWindowViewModel.SelectedNavItem):
            case nameof(MainWindowViewModel.SelectedBottomNavItem):
            case nameof(MainWindowViewModel.CurrentPage):
                SyncSelectionAndNavigate();
                break;
        }
    }

    // ===== FluentAvalonia NavigationView 导航 =====

    private void OnMainNavLoaded(object? sender, RoutedEventArgs e)
    {
        if (_navInitialized || _vm == null) return;
        _navInitialized = true;

        SyncSelectionAndNavigate();
    }

    private void OnNavSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (_vm == null) return;

        if (e.SelectedItem is NavItemViewModel item &&
            !ReferenceEquals(_vm.SelectedNavItem, item) &&
            !ReferenceEquals(_vm.SelectedBottomNavItem, item))
        {
            if (_vm.NavItems.Contains(item))
            {
                _vm.SelectedNavItem = item;
            }
            else if (_vm.BottomNavItems.Contains(item))
            {
                _vm.SelectedBottomNavItem = item;
            }
        }

        Dispatcher.UIThread.Post(FixSelectionIndicatorGhosts);
    }

    private void SyncSelectionAndNavigate()
    {
        if (_vm == null) return;

        var entry = _vm.SelectedNavEntry;
        if (entry != null && !ReferenceEquals(MainNav.SelectedItem, entry))
        {
            MainNav.SelectedItem = entry;
        }

        var page = _vm.CurrentPage;
        if (page == null) return;

        if (MainFrame.Content is Control current && ReferenceEquals(current.DataContext, page))
        {
            return;
        }

        MainFrame.NavigateFromObject(page, new FrameNavigationOptions
        {
            IsNavigationStackEnabled = false,
            TransitionInfoOverride = new EntranceNavigationTransitionInfo()
        });
    }

    // FluentAvalonia 2.4.1 中，取消选中的项其 SelectionIndicator 透明度不会复位，
    // 导致切换时旧选项的绿色指示条残留（拖影）。这里手动同步指示条透明度。
    private void FixSelectionIndicatorGhosts()
    {
        var selected = MainNav.SelectedItem;

        foreach (var presenter in MainNav.GetVisualDescendants().OfType<NavigationViewItemPresenter>())
        {
            var hostItem = presenter.FindAncestorOfType<NavigationViewItem>();
            if (hostItem == null) continue;

            var indicator = presenter.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "SelectionIndicator");
            if (indicator != null)
            {
                indicator.Opacity = selected != null &&
                                    (ReferenceEquals(hostItem.DataContext, selected) ||
                                     ReferenceEquals(hostItem.Content, selected))
                    ? 1
                    : 0;
            }
        }
    }

    // ===== 通知位置 =====

    private void UpdateNotificationPosition()
    {
        if (_vm == null) return;

        if (NotificationItemsControl == null) return;

        if (_vm.NotificationPosition == NotificationPosition.BottomRight)
        {
            NotificationItemsControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            NotificationItemsControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            NotificationItemsControl.Margin = new Thickness(0, 0, 16, 16);
        }
        else
        {
            NotificationItemsControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            NotificationItemsControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            NotificationItemsControl.Margin = new Thickness(0, 12, 0, 0);
        }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // 防重入：desktop.Shutdown() 会再次对本窗口 CloseCore 并再次触发 Closing，
        // 不拦截会形成 关闭→Shutdown→再关闭 的无限递归，最终栈溢出。
        if (_shutdownRequested) return;

        // 崩溃流程会主动关闭主窗口：此时不能触发退出，否则崩溃窗口来不及显示
        if (Application.Current is App app && app.IsCrashFlowActive) return;

        // ShutdownMode 为 OnExplicitShutdown：关闭主窗口即视为退出应用。
        // 系统标题栏的关闭按钮与“启动后关闭启动器”设置都走这里，避免进程残留。
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _shutdownRequested = true;
            desktop.Shutdown();
        }
    }

    private void AuthUrlOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm?.Dialogs?.IsAuthUrlOpen == true)
        {
            _vm.Dialogs.CloseAuthUrlCommand.Execute(true);
        }
    }
}
