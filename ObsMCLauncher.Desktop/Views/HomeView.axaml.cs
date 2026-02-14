using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is HomeCardInfo card)
        {
            var viewModel = this.DataContext as HomeViewModel;
            viewModel?.CardClickCommand.Execute(card);
        }
    }
}
