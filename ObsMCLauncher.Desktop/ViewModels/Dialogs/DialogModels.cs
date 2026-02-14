using System;
using System.Threading.Tasks;

namespace ObsMCLauncher.Desktop.ViewModels.Dialogs;

public enum DialogType
{
    Info,
    Success,
    Warning,
    Error,
    Question,
    Input
}

public enum DialogButtons
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum DialogResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public sealed class DialogRequest
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string InputText { get; set; } = string.Empty;

    public string Placeholder { get; init; } = string.Empty;

    public DialogType Type { get; init; }

    public DialogButtons Buttons { get; init; }

    public TaskCompletionSource<(DialogResult Result, string Text)> Completion { get; } = new();
}

public sealed class UpdateDialogRequest
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string Title { get; init; } = string.Empty;

    public string MarkdownContent { get; init; } = string.Empty;

    public string ConfirmText { get; init; } = "确定";

    public string CancelText { get; init; } = "取消";

    public TaskCompletionSource<bool> Completion { get; } = new();
}
