using Avalonia.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Windows;

public partial class CrashWindow : Window
{
    private void TitleBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public CrashWindow()
    {
        InitializeComponent();
    }

    public CrashWindow(string summary, string crashReport) : this()
    {
        DataContext = new CrashWindowViewModel(summary, crashReport, this);
    }
}
