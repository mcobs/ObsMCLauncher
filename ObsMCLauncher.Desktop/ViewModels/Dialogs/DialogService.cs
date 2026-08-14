using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace ObsMCLauncher.Desktop.ViewModels.Dialogs;

public partial class DialogService : ObservableObject
{
    [ObservableProperty]
    private AuthUrlDialogRequest? authUrlCurrent;

    [ObservableProperty]
    private UpdateDialogRequest? updateDialogCurrent;

    public bool IsAuthUrlOpen => AuthUrlCurrent != null;

    public bool IsUpdateDialogOpen => UpdateDialogCurrent != null;

    public bool IsAnyModalOpen => IsAuthUrlOpen || IsUpdateDialogOpen;

    // ===== 常规对话框：改用 FluentAvalonia ContentDialog =====

    public Task<DialogResult> ShowAsync(string title, string message, DialogType type, DialogButtons buttons)
        => Dispatcher.UIThread.InvokeAsync(() => ShowContentDialogAsync(title, message, type, buttons, null));

    public Task<(DialogResult Result, string Text)> ShowInputAsync(
        string title,
        string message,
        string defaultText,
        string placeholder = "",
        DialogButtons buttons = DialogButtons.OKCancel)
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var textBox = new TextBox
            {
                Text = defaultText ?? string.Empty,
                Watermark = placeholder ?? string.Empty,
                MinWidth = 320
            };

            var result = await ShowContentDialogAsync(title, message, DialogType.Input, buttons, textBox);
            return (result, textBox.Text ?? string.Empty);
        });

    public Task<DialogResult> ShowInfo(string title, string message, DialogButtons buttons = DialogButtons.OK)
        => ShowAsync(title, message, DialogType.Info, buttons);

    public Task<DialogResult> ShowSuccess(string title, string message, DialogButtons buttons = DialogButtons.OK)
        => ShowAsync(title, message, DialogType.Success, buttons);

    public Task<DialogResult> ShowWarning(string title, string message, DialogButtons buttons = DialogButtons.OK)
        => ShowAsync(title, message, DialogType.Warning, buttons);

    public Task<DialogResult> ShowError(string title, string message, DialogButtons buttons = DialogButtons.OK)
        => ShowAsync(title, message, DialogType.Error, buttons);

    public Task<DialogResult> ShowQuestion(string title, string message, DialogButtons buttons = DialogButtons.YesNo)
        => ShowAsync(title, message, DialogType.Question, buttons);

    private static async Task<DialogResult> ShowContentDialogAsync(
        string title,
        string message,
        DialogType type,
        DialogButtons buttons,
        Control? content)
    {
        var window = GetMainWindow();
        if (window == null)
            return DialogResult.None;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content ?? CreateMessageContent(message)
        };

        ConfigureButtons(dialog, buttons);

        var result = await dialog.ShowAsync(window);
        return MapResult(result, buttons);
    }

    private static Control CreateMessageContent(string message)
    {
        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        };
    }

    private static void ConfigureButtons(ContentDialog dialog, DialogButtons buttons)
    {
        switch (buttons)
        {
            case DialogButtons.OK:
                dialog.PrimaryButtonText = "确定";
                dialog.DefaultButton = ContentDialogButton.Primary;
                break;

            case DialogButtons.OKCancel:
                dialog.PrimaryButtonText = "确定";
                dialog.CloseButtonText = "取消";
                dialog.DefaultButton = ContentDialogButton.Primary;
                break;

            case DialogButtons.YesNo:
                dialog.PrimaryButtonText = "是";
                dialog.SecondaryButtonText = "否";
                dialog.DefaultButton = ContentDialogButton.Primary;
                break;

            case DialogButtons.YesNoCancel:
                dialog.PrimaryButtonText = "是";
                dialog.SecondaryButtonText = "否";
                dialog.CloseButtonText = "取消";
                dialog.DefaultButton = ContentDialogButton.Primary;
                break;
        }
    }

    private static DialogResult MapResult(ContentDialogResult result, DialogButtons buttons)
    {
        return result switch
        {
            ContentDialogResult.Primary => buttons is DialogButtons.OK or DialogButtons.OKCancel
                ? DialogResult.OK
                : DialogResult.Yes,
            ContentDialogResult.Secondary => DialogResult.No,
            _ => DialogResult.Cancel
        };
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    // ===== 授权链接对话框（保留自定义实现） =====

    public Task<bool> ShowAuthUrlAsync(string url, string title = "微软账户登录")
    {
        if (IsAnyModalOpen)
        {
            return Task.FromResult(false);
        }

        var req = new AuthUrlDialogRequest
        {
            Url = url,
            Title = title
        };

        AuthUrlCurrent = req;
        OnPropertyChanged(nameof(IsAuthUrlOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));

        return req.Completion.Task;
    }

    [RelayCommand]
    private async Task CopyAuthUrlAsync()
    {
        if (AuthUrlCurrent == null)
            return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                await desktop.MainWindow.Clipboard!.SetTextAsync(AuthUrlCurrent.Url);
            }
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void CloseAuthUrl(bool cancelled)
    {
        if (AuthUrlCurrent == null)
            return;

        var req = AuthUrlCurrent;
        AuthUrlCurrent = null;
        OnPropertyChanged(nameof(IsAuthUrlOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));

        req.Completion.TrySetResult(!cancelled);
    }

    // ===== 更新对话框（保留自定义实现） =====

    public Task<bool> ShowUpdateDialogAsync(string title, string markdownContent, string confirmText = "确定", string cancelText = "取消")
    {
        if (IsAnyModalOpen)
        {
            return Task.FromResult(false);
        }

        var req = new UpdateDialogRequest
        {
            Title = title,
            MarkdownContent = markdownContent,
            ConfirmText = confirmText,
            CancelText = cancelText
        };

        UpdateDialogCurrent = req;
        OnPropertyChanged(nameof(IsUpdateDialogOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));

        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            req.StartEnterAnimation();
        }, TimeSpan.FromMilliseconds(30));

        return req.Completion.Task;
    }

    [RelayCommand]
    private async Task CloseUpdateDialogAsync(bool confirmed)
    {
        if (UpdateDialogCurrent == null)
            return;

        var req = UpdateDialogCurrent;
        req.StartExitAnimation();
        await Task.Delay(150);
        UpdateDialogCurrent = null;
        OnPropertyChanged(nameof(IsUpdateDialogOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));

        req.Completion.TrySetResult(confirmed);
    }

    /// <summary>
    /// 重新显示更新对话框（用于下载进度展示）
    /// </summary>
    public void ReopenUpdateDialog()
    {
        if (UpdateDialogCurrent == null) return;
        OnPropertyChanged(nameof(IsUpdateDialogOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));
    }

    partial void OnAuthUrlCurrentChanged(AuthUrlDialogRequest? value)
    {
        OnPropertyChanged(nameof(IsAuthUrlOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));
    }
}
