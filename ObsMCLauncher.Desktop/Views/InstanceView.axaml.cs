using System;
using Avalonia.Controls;
using ObsMCLauncher.Desktop.Controls;
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

        // 弹窗实例在 XAML 里只建一次、反复开合。FA 关窗时会留下 IsHitTestVisible=false 且不再恢复，
        // 直接调 ShowAsync() 的话第二次打开就会「看得见但点不动」，必须走这个包装（见 ContentDialogReuse）。
        await GroupManagerDialog.ShowReusedAsync();
    }
}
