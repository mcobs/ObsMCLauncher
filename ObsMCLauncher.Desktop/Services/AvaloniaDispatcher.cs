using System;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Services.Ui;

namespace ObsMCLauncher.Desktop.Services;

public sealed class AvaloniaDispatcher : IDispatcher
{
    public void Post(Action action)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    public async Task InvokeAsync(Action action)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action);
    }
}
