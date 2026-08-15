using Avalonia.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Windows;

public partial class CrashWindow : Window
{
    public CrashWindow()
    {
        InitializeComponent();
    }

    public CrashWindow(string summary, string crashReport) : this()
    {
        DataContext = new CrashWindowViewModel(summary, crashReport, this);
    }
}
