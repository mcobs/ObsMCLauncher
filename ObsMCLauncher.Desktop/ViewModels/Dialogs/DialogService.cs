using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;

namespace ObsMCLauncher.Desktop.ViewModels.Dialogs;

public partial class DialogService : ObservableObject
{
    [ObservableProperty]
    private UpdateDialogRequest? updateDialogCurrent;

    public bool IsUpdateDialogOpen => UpdateDialogCurrent != null;

    public bool IsAnyModalOpen => IsUpdateDialogOpen;

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

    // ===== 授权链接对话框：走 FluentAvalonia ContentDialog =====

    private ContentDialog? _authDialog;
    private bool _authCompleted;

    /// <summary>
    /// 显示「浏览器已打开授权页」的对话框并等待其关闭。
    /// 返回 true = 授权流程自己关掉的，false = 用户手动关闭/取消。
    /// </summary>
    public Task<bool> ShowAuthUrlAsync(string url, string title = "微软账户登录")
        => Dispatcher.UIThread.InvokeAsync(() => ShowAuthUrlCoreAsync(url, title));

    private async Task<bool> ShowAuthUrlCoreAsync(string url, string title)
    {
        var window = GetMainWindow();
        if (window == null || _authDialog != null) return false;

        var hint = new TextBlock
        {
            Text = "浏览器应已自动打开，如果没有，请手动复制下方链接并在浏览器中打开。完成授权后此对话框会自动关闭。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            MaxWidth = 460
        };
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");

        var caption = new TextBlock { Text = "授权链接", FontSize = 11 };
        caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextTertiaryBrush");

        var link = new SelectableTextBlock
        {
            Text = url,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 12,
            MaxHeight = 120
        };
        link[!SelectableTextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush");

        var linkBox = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = link
        };
        linkBox[!Border.BackgroundProperty] = new DynamicResourceExtension("BackgroundBrush");
        linkBox[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");

        // 复制按钮放在内容里而不是当对话框主按钮：点了不该关闭对话框
        var copy = new Button { Content = "复制授权链接", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        copy.Classes.Add("primary");
        copy.Click += async (_, _) =>
        {
            try { await window.Clipboard!.SetTextAsync(url); } catch { /* 剪贴板占用时忽略 */ }
        };

        var content = new StackPanel { Spacing = 12, Width = 460 };
        content.Children.Add(hint);
        content.Children.Add(caption);
        content.Children.Add(linkBox);
        content.Children.Add(copy);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.None
        };

        _authDialog = dialog;
        _authCompleted = false;
        try { await dialog.ShowAsync(window); }
        finally { _authDialog = null; }

        return _authCompleted;
    }

    /// <summary>关闭授权对话框（cancelled=true 表示用户取消 → 返回 false）</summary>
    [RelayCommand]
    private void CloseAuthUrl(bool cancelled)
    {
        if (_authDialog is not { } dialog) return;

        _authCompleted = !cancelled;
        dialog.Hide();
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

}
