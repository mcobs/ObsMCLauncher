using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class WelcomeLicensePageView : UserControl
{
    public WelcomeLicensePageView()
    {
        InitializeComponent();
    }

    private async void ButtonShowOssLicense_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WelcomeLicensePageViewModel vm)
            return;

        var dialog = new ContentDialog
        {
            Title = "开放源代码许可",
            // ContentDialog 自带内容滚动，这里不能再套 ScrollViewer，否则出现双重滚动条
            Content = new TextBlock
            {
                Text = vm.LicenseText,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "关闭",
            DefaultButton = ContentDialogButton.Primary
        };

        await dialog.ShowAsync(TopLevel.GetTopLevel(this)!);
    }
}
