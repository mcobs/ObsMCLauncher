using Avalonia.Controls;
using Avalonia.Input;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Windows;

public partial class DevConsoleWindow : Window
{
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public DevConsoleWindow()
    {
        InitializeComponent();
        DataContext = new DevConsoleViewModel(this);
    }

    private void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DevConsoleViewModel vm)
        {
            vm.ExecuteCommand.Execute(null);
        }
    }
}
