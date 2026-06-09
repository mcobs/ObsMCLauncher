using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private readonly Dictionary<string, Point> _swipeStartPoints = new();
    private readonly HashSet<string> _swipeActive = new();

    // 导航栏 hover-intent 防抖
    private readonly DispatcherTimer _hoverTimer;
    private ListBoxItem? _pendingHoverItem;

    public MainWindow()
    {
        InitializeComponent();

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hoverTimer.Tick += OnHoverTimerTick;

        PropertyChanged += OnWindowPropertyChanged;
        DataContextChanged += (_, _) => HookNavCollapsed();
        HookNavCollapsed();

        if (_vm != null)
        {
            _vm.WindowWidth = Width;
        }

        NavListBox.ContainerPrepared += OnNavContainerPrepared;
        BottomNavListBox.ContainerPrepared += OnNavContainerPrepared;
        NavListBox.SelectionChanged += OnNavSelectionChanged;
        BottomNavListBox.SelectionChanged += OnNavSelectionChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WidthProperty && _vm != null)
        {
            _vm.WindowWidth = Width;
        }
    }

    private void HookNavCollapsed()
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
        }

        _vm = DataContext as MainWindowViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += VmOnPropertyChanged;
        }

        UpdateNotificationPosition();
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.NotificationPosition))
        {
            Dispatcher.UIThread.Post(UpdateNotificationPosition);
        }
    }

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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Close();
        }
    }

    private void AuthUrlOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm?.Dialogs?.IsAuthUrlOpen == true)
        {
            _vm.Dialogs.CloseAuthUrlCommand.Execute(true);
        }
    }

    private static NotificationItemViewModel? GetNotificationVm(object? sender)
    {
        return (sender as Control)?.DataContext as NotificationItemViewModel;
    }

    private void OnNotificationTapped(object? sender, TappedEventArgs e)
    {
        var vm = GetNotificationVm(sender);
        if (vm?.Position == NotificationPosition.BottomRight)
        {
            vm.CloseCommand.Execute(null);
        }
    }

    private void OnNotificationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = GetNotificationVm(sender);
        if (vm?.Position != NotificationPosition.BottomRight) return;

        var point = e.GetCurrentPoint(sender as Control);
        if (!point.Properties.IsLeftButtonPressed) return;

        _swipeStartPoints[vm.Id] = point.Position;
        _swipeActive.Add(vm.Id);
    }

    private void OnNotificationPointerMoved(object? sender, PointerEventArgs e)
    {
        var vm = GetNotificationVm(sender);
        if (vm?.Position != NotificationPosition.BottomRight) return;
        if (!_swipeActive.Contains(vm.Id)) return;
        if (!_swipeStartPoints.TryGetValue(vm.Id, out var start)) return;

        var current = e.GetPosition(sender as Control);
        var deltaX = current.X - start.X;

        if (deltaX > 5)
        {
            vm.UpdateSwipeDrag(deltaX);
        }
    }

    private void OnNotificationPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var vm = GetNotificationVm(sender);
        if (vm?.Position != NotificationPosition.BottomRight) return;
        if (!_swipeActive.Contains(vm.Id)) return;

        _swipeActive.Remove(vm.Id);

        double totalDeltaX = 0;
        if (_swipeStartPoints.TryGetValue(vm.Id, out var start))
        {
            var current = e.GetPosition(sender as Control);
            totalDeltaX = current.X - start.X;
        }
        _swipeStartPoints.Remove(vm.Id);

        vm.EndSwipeDrag(totalDeltaX);
    }

    // ===== 导航栏 hover-intent 防抖 =====

    private void OnNavContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem item)
        {
            item.PointerEntered += OnNavPointerEntered;
            item.PointerExited += OnNavPointerExited;
        }
    }

    private void OnNavPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBoxItem item || item.IsSelected) return;

        CancelPendingHover();
        _pendingHoverItem = item;
        _hoverTimer.Start();
    }

    private void OnNavPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBoxItem item) return;

        RemoveHoverClass(item);

        if (_pendingHoverItem == item)
        {
            CancelPendingHover();
        }
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 选中切换时立即取消待处理的悬浮动画，避免与 :selected 样式冲突闪烁
        CancelPendingHover();
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        var item = _pendingHoverItem;
        _pendingHoverItem = null;

        if (item is { IsSelected: false })
        {
            ApplyHoverClass(item);
        }
    }

    private void CancelPendingHover()
    {
        _hoverTimer.Stop();
        if (_pendingHoverItem != null)
        {
            RemoveHoverClass(_pendingHoverItem);
            _pendingHoverItem = null;
        }
    }

    private static void ApplyHoverClass(ListBoxItem item)
    {
        var border = item.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("nav-item"));
        border?.Classes.Add("nav-item-hovered");
    }

    private static void RemoveHoverClass(ListBoxItem item)
    {
        var border = item.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("nav-item"));
        border?.Classes.Remove("nav-item-hovered");
    }
}
