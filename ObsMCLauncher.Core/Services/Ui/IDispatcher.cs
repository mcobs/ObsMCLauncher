using System;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services.Ui;

public interface IDispatcher
{
    void Post(Action action);

    Task InvokeAsync(Action action);
}
