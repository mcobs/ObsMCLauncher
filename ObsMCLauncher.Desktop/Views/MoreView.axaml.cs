using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.Views;

public partial class MoreView : UserControl
{
    public MoreView()
    {
        InitializeComponent();
    }

    private void TitleText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MoreViewModel moreVm && moreVm.About is AboutViewModel aboutVm)
        {
            aboutVm.OnTitleClick();
        }
    }
}
