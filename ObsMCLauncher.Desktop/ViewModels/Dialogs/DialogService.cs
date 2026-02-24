using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels.Dialogs;

public partial class DialogService : ObservableObject
{
    [ObservableProperty]
    private DialogRequest? current;

    [ObservableProperty]
    private AuthUrlDialogRequest? authUrlCurrent;

    [ObservableProperty]
    private UpdateDialogRequest? updateDialogCurrent;

    public bool IsOpen => Current != null;

    public bool IsAuthUrlOpen => AuthUrlCurrent != null;

    public bool IsUpdateDialogOpen => UpdateDialogCurrent != null;

    public bool IsAnyModalOpen => IsOpen || IsAuthUrlOpen || IsUpdateDialogOpen;

    public Task<DialogResult> ShowAsync(string title, string message, DialogType type, DialogButtons buttons)
    {
        if (IsAnyModalOpen)
        {
            return Task.FromResult(DialogResult.None);
        }

        var req = new DialogRequest
        {
            Title = title,
            Message = message,
            Type = type,
            Buttons = buttons
        };

        Current = req;
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));

        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            req.StartEnterAnimation();
        }, TimeSpan.FromMilliseconds(30));

        return req.Completion.Task.ContinueWith(t => t.Result.Result, TaskScheduler.Default);
    }

    public Task<(DialogResult Result, string Text)> ShowInputAsync(
        string title,
        string message,
        string defaultText,
        string placeholder = "",
        DialogButtons buttons = DialogButtons.OKCancel)
    {
        if (IsAnyModalOpen)
        {
            return Task.FromResult((DialogResult.None, ""));
        }

        var req = new DialogRequest
        {
            Title = title,
            Message = message,
            Type = DialogType.Input,
            Buttons = buttons,
            InputText = defaultText ?? string.Empty,
            Placeholder = placeholder ?? string.Empty
        };

        Current = req;
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));

        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            req.StartEnterAnimation();
        }, TimeSpan.FromMilliseconds(30));

        return req.Completion.Task;
    }

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
    private async void Choose(DialogResult result)
    {
        if (Current == null)
            return;

        var req = Current;
        req.StartExitAnimation();
        await Task.Delay(150);
        Current = null;
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));

        req.Completion.TrySetResult((result, req.InputText));
    }

    [RelayCommand]
    private void Close()
    {
        Choose(DialogResult.Cancel);
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

    [RelayCommand]
    private async void CloseUpdateDialog(bool confirmed)
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

    partial void OnCurrentChanged(DialogRequest? value)
    {
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));
    }

    partial void OnAuthUrlCurrentChanged(AuthUrlDialogRequest? value)
    {
        OnPropertyChanged(nameof(IsAuthUrlOpen));
        OnPropertyChanged(nameof(IsAnyModalOpen));
    }
}
