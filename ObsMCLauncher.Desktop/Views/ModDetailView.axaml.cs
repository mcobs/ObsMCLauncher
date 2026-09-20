using Avalonia.Controls;
using Avalonia.Input;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class ModDetailView : UserControl
{
    public ModDetailView()
    {
        InitializeComponent();
    }

    /// <summary>点击前置资源行的任意位置即可进入该前置的详情页</summary>
    private void OnDependencyRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: DependencyItemViewModel dependency }) return;
        if (DataContext is not ModDetailViewModel vm) return;
        if (!vm.NavigateToDependencyCommand.CanExecute(dependency)) return;

        vm.NavigateToDependencyCommand.Execute(dependency);
        e.Handled = true;
    }
}
