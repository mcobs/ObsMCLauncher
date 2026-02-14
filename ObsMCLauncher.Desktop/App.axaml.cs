using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.Views;
using ObsMCLauncher.Desktop.Windows;

namespace ObsMCLauncher.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            ObsMCLauncher.Core.Bootstrap.LauncherBootstrap.Initialize();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            SetupExceptionHandling(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupExceptionHandling(IClassicDesktopStyleApplicationLifetime desktop)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            ShowCrashWindow(e.ExceptionObject as Exception);
        };

        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (s, e) =>
        {
            ShowCrashWindow(e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            ShowCrashWindow(e.Exception);
            e.SetObserved();
        };
    }

    private void ShowCrashWindow(Exception? exception)
    {
        if (exception == null) return;

        var summary = exception.Message ?? "未知错误";
        var report = $@"=== ObsMCLauncher 崩溃报告 ===
时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
版本: {Core.Utils.VersionInfo.DisplayVersion}
操作系统: {Environment.OSVersion}
运行时: {Environment.Version}

=== 异常类型 ===
{exception.GetType().FullName}

=== 异常消息 ===
{exception.Message}

=== 堆栈跟踪 ===
{exception.StackTrace}

=== 内部异常 ===
{(exception.InnerException != null ? $"{exception.InnerException.GetType().FullName}: {exception.InnerException.Message}\n{exception.InnerException.StackTrace}" : "无")}
";

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // 崩溃窗口出现后，销毁主界面和其他窗口（保持 crash 指令预览窗口除外）
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    foreach (var w in desktop.Windows.ToList())
                    {
                        // 关闭所有非 CrashWindow 的窗口
                        if (w is not CrashWindow)
                        {
                            try { w.Close(); } catch { }
                        }
                    }

                    // 确保主窗口引用被清理，避免残留
                    try { desktop.MainWindow = null; } catch { }
                }
            }
            catch { }

            var crashWindow = new CrashWindow(summary, report);
            crashWindow.Show();
        });
    }

    // 仅用于开发者控制台的 crash 指令：不销毁其他窗口
    public static void ShowCrashWindowPreview(string summary, string report)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var crashWindow = new CrashWindow(summary, report);
            crashWindow.Show();
        });
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}