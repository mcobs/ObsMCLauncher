using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

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
        Close();
    }
}
