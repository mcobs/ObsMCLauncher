using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class YggdrasilLoginWindow : Window
{
    public YggdrasilLoginWindow()
    {
        InitializeComponent();
    }

    public YggdrasilLoginWindow(YggdrasilLoginViewModel vm) : this()
    {
        DataContext = vm;

        vm.OnLoginCompleted = account =>
        {
            // 关闭窗口并把结果传回调用方
            Close(account);
        };
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
