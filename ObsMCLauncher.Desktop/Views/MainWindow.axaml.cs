using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private readonly Dictionary<string, Point> _swipeStartPoints = new();
    private readonly HashSet<string> _swipeActive = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => HookNavCollapsed();
        HookNavCollapsed();
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

        UpdateNavWidth();
        UpdateNavLayout();
        UpdateNotificationPosition();
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsNavCollapsed))
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateNavWidth();
                UpdateNavLayout();
            });
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.NotificationPosition))
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

    private void UpdateNavWidth()
    {
        if (_vm == null)
            return;

        if (RootGrid.ColumnDefinitions.Count < 2)
            return;

        RootGrid.ColumnDefinitions[0].Width = _vm.IsNavCollapsed ? new GridLength(72) : new GridLength(200);
    }

    private void UpdateNavLayout()
    {
        if (_vm == null)
            return;

        // 折叠时收紧导航列表左右留白，展开时恢复
        if (NavListBox != null)
        {
            NavListBox.Margin = _vm.IsNavCollapsed ? new Thickness(0) : new Thickness(10, 0);
        }

        // 折叠时减少导航区域整体边距，贴近 WPF 的紧凑模式
        if (NavBorder != null)
        {
            NavBorder.Padding = _vm.IsNavCollapsed ? new Thickness(0) : new Thickness(0);
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
}
