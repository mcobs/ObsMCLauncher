using System;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.Views;
using ObsMCLauncher.Desktop.Views.SettingsPages;
using ObsMCLauncher.Desktop.Windows;

namespace ObsMCLauncher.Desktop;

public partial class App : Application
{
    private readonly object _crashLock = new();
    private bool _crashWindowShowing;
    private bool _crashExitRequested;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var config = ObsMCLauncher.Core.Models.LauncherConfig.Load();

            // 强制 FluentAvalonia 使用强调色，避免回退到系统蓝
            var faTheme = Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
            if (faTheme != null)
            {
                faTheme.CustomAccentColor = ParseAccentColor(config.AccentColor);
            }

            // 设置为显式关闭模式，防止异常时应用自动退出
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            SetupExceptionHandling(desktop);

            ObsMCLauncher.Core.Bootstrap.LauncherBootstrap.Initialize();

            ObsMCLauncher.Core.Services.UpdateService.Initialize(config.UpdateChannel);

            // 首次启动：默认使用深色主题（0=深色），并立即落盘
            // （必须在创建 MainWindowViewModel 之前，主题由 SettingsViewModel 构造时应用）
            if (!config.WelcomeCompleted && config.ThemeMode != 0)
            {
                config.ThemeMode = 0;
                config.Save();
            }

            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            if (!config.WelcomeCompleted)
            {
                // 首次启动：只显示欢迎窗口，流程完成后才显示主界面
                var welcome = new WelcomeWindow(isFirstRun: true);
                welcome.Closed += (_, _) =>
                {
                    if (welcome.IsCompleted)
                    {
                        // MainWindow 是普通属性，运行期重新赋值不会自动 Show，需手动显示
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();
                    }
                    else if (!IsCrashFlowActive)
                    {
                        // 未完成流程即关闭欢迎窗口：视为取消启动
                        desktop.Shutdown();
                    }
                };
                desktop.MainWindow = welcome;
            }
            else
            {
                desktop.MainWindow = mainWindow;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>把配置中的强调色解析为 Color，非法或为空回退默认绿</summary>
    private static Color ParseAccentColor(string? hex)
        => Color.TryParse(hex, out var c) ? c : Color.Parse("#10B981");

    /// <summary>开发者控制台 welcome 指令：手动打开欢迎窗口（不影响完成标记）。</summary>
    public static void ShowWelcomeWindow()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var welcome = new WelcomeWindow(isFirstRun: false);
            welcome.Show();
        });
    }

    /// <summary>崩溃窗口流程是否激活（此时关闭其他窗口不应触发退出逻辑）</summary>
    public bool IsCrashFlowActive
    {
        get
        {
            lock (_crashLock)
            {
                return _crashWindowShowing;
            }
        }
    }

    private void SetupExceptionHandling(IClassicDesktopStyleApplicationLifetime desktop)    {
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

        lock (_crashLock)
        {
            if (_crashWindowShowing) return;
            _crashWindowShowing = true;
        }

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
            crashWindow.Closed += (_, _) =>
            {
                lock (_crashLock)
                {
                    _crashWindowShowing = false;
                }

                // 崩溃窗口关闭即退出应用（OnExplicitShutdown 不会自动退出）。
                // 防重入：Shutdown 会再次关闭本窗口，避免重复触发。
                if (!_crashExitRequested)
                {
                    _crashExitRequested = true;
                    (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(-1);
                }
            };
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

    // 主页卡片点击（模板定义在 App 级，真实主页与设置页模拟器共用）
    public void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: HomeComponentViewModel { Card: { } card } vm })
        {
            return;
        }

        // 编辑器预览里的点击只用于选择组件，不触发卡片命令
        if (sender is Control c && c.FindAncestorOfType<SettingsHomePage>() != null)
        {
            return;
        }

        vm.Owner.CardClickCommand.Execute(card);
    }
}