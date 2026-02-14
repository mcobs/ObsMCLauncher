using Avalonia.Controls;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.Save();
        }
    }

    private void Reload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.Reload();
        }
    }

    private void CardEnabledChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.DataContext is HomeCardInfo card && DataContext is SettingsViewModel vm)
        {
            card.IsEnabled = toggle.IsChecked ?? true;
            vm.OnCardEnabledChanged(card);
        }
    }
}
