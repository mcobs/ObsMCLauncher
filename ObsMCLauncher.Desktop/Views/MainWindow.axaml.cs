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

        PropertyChanged += OnWindowPropertyChanged;
        DataContextChanged += (_, _) => HookNavCollapsed();
        HookNavCollapsed();

        if (_vm != null)
        {
            _vm.WindowWidth = Width;
        }
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
}
