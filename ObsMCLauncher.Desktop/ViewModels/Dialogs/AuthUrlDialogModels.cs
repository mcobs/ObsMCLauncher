using System;
using System.Threading.Tasks;

namespace ObsMCLauncher.Desktop.ViewModels.Dialogs;

public sealed class AuthUrlDialogRequest
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string Url { get; init; } = string.Empty;

    public string Title { get; init; } = "微软账户登录";

    public TaskCompletionSource<bool> Completion { get; } = new();
}
