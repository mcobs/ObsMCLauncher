using Avalonia.Controls;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views.SettingsPages;

public partial class SettingsGeneralPage : UserControl
{
    public SettingsGeneralPage()
    {
        InitializeComponent();
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
