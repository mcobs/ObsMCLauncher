using System;
using Avalonia.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class InstanceView : UserControl
{
    private InstanceViewModel? _currentVm;

    public InstanceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
            _currentVm.GroupManagerRequested -= OnGroupManagerRequested;

        _currentVm = DataContext as InstanceViewModel;

        if (_currentVm != null)
            _currentVm.GroupManagerRequested += OnGroupManagerRequested;
    }

    private async void OnGroupManagerRequested()
    {
        // ContentDialog 内容在弹出层中渲染，会丢失 DataContext 继承（回退到 MainWindow 的 DataContext），
        // 故在显示前显式绑定到当前 InstanceViewModel。
        GroupManagerDialog.DataContext = DataContext;
        await GroupManagerDialog.ShowAsync();
    }
}
