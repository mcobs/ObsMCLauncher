using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Services.Minecraft;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class DevConsoleViewModel : ObservableObject
{
    private readonly Window _window;
    private readonly Dictionary<string, Action<string[]>> _commands = new();

    [ObservableProperty]
    private string _output = "ObsMCLauncher DevConsole [版本 1.0.0]\r\n(c) 2026 ObsMCLauncher. 保留所有权利。\r\n\r\n输入 'help' 以查看命令列表。\r\n";

    [ObservableProperty]
    private string _command = string.Empty;

    public DevConsoleViewModel(Window window)
    {
        _window = window;
        RegisterCommands();
    }

    private void RegisterCommands()
    {
        _commands["help"] = _ => ShowHelp();
        _commands["?"] = _ => ShowHelp();
        _commands["clear"] = _ => Output = string.Empty;
        _commands["crash"] = _ => ShowCrash();
        _commands["throw"] = args => ThrowException(args);
        _commands["update"] = args => ShowUpdateDialog(args);
        _commands["welcome"] = args => ShowWelcomeCommand(args);
        _commands["notify"] = args => ShowNotifyCommand(args);
    }

    private void ShowHelp()
    {
        var help = @"可用命令:
  help                 显示帮助
  ?                    显示帮助
  clear                清空输出
  crash                直接打开崩溃窗口（不抛未处理异常）
  throw <msg>          抛出一个未处理异常（msg 可选）
  update [tag]         测试更新对话框（tag 可选，默认 v9.9.9）
  welcome              打开欢迎窗口（不影响完成标记）
  welcome reset        重置欢迎界面完成标记（下次启动重新显示）
  notify [类型]        发送测试通知（info/success/warning/error/progress/countdown，默认全部）
";
        AppendOutput(help);
    }

    private void ShowCrash()
    {
        try
        {
            var summary = "手动打开崩溃窗口（crash 指令）";
            var report = string.Join(Environment.NewLine, new[]
            {
                "========== ObsMCLauncher 崩溃报告 ==========",
                $"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                "标题: 手动打开崩溃窗口（crash 指令）",
                "版本: 1.0.0-Avalonia",
                $"系统: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
                $"架构: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
                $"运行目录: {AppDomain.CurrentDomain.BaseDirectory}",
                "",
                "---------- 异常信息 ----------",
                "(crash 指令不会抛出未处理异常；此窗口用于预览/验证导出与复制功能)"
            });

            App.ShowCrashWindowPreview(summary, report);
            AppendOutput("[info] 已打开崩溃窗口");
        }
        catch (Exception ex)
        {
            AppendOutput($"[error] 打开崩溃窗口失败: {ex.Message}");
        }
    }

    private void ThrowException(string[] args)
    {
        var msg = args.Length > 0 ? string.Join(' ', args) : "手动抛出异常";
        AppendOutput("[info] 已触发 throw（异常将由全局捕获处理）");
        Dispatcher.UIThread.Post(() => {
            throw new Exception(msg);
        });
    }

    private void ShowUpdateDialog(string[] args)
    {
        _ = ShowUpdateDialogAsync(args);
    }

    private async System.Threading.Tasks.Task ShowUpdateDialogAsync(string[] args)
    {
        var tag = args.Length > 0 ? args[0] : "v9.9.9";
        AppendOutput($"[info] 正在打开更新对话框: {tag}");

        try
        {
            var dialogs = NavigationStore.MainWindow?.Dialogs;
            if (dialogs == null)
            {
                AppendOutput("[error] 无法获取 DialogService");
                return;
            }

            var markdownContent = $@"# 🎉 发现新版本 {tag}

## 更新内容

### ✨ 新功能
- 添加了全新的用户界面设计
- 支持多账号快速切换
- 新增模组包一键安装功能
- 优化了下载速度和稳定性

### 🐛 修复
- 修复了启动游戏时偶发的崩溃问题
- 修复了账号登录状态异常
- 修复了部分模组无法正确识别的问题

### 🔧 优化
- 大幅提升了启动速度
- 减少了内存占用
- 改进了日志输出格式

---

**当前版本**: {VersionInfo.DisplayVersion}
**最新版本**: {tag}
**发布时间**: {DateTime.Now:yyyy-MM-dd}

点击「立即更新」前往下载页面。
";

            var result = await dialogs.ShowUpdateDialogAsync($"发现新版本 {tag}", markdownContent, "立即更新", "稍后提醒");

            if (result)
            {
                AppendOutput("[info] 用户点击了「立即更新」");
                UpdateService.OpenLatestReleasePage();
            }
            else
            {
                AppendOutput("[info] 用户关闭了更新对话框");
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"[error] 打开更新对话框失败: {ex.Message}");
        }
    }

    private void ShowWelcomeCommand(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            var config = LauncherConfig.Load();
            config.WelcomeCompleted = false;
            config.Save();
            AppendOutput("[info] 已重置欢迎界面完成标记，下次启动将重新显示欢迎窗口");
            return;
        }

        try
        {
            App.ShowWelcomeWindow();
            AppendOutput("[info] 已打开欢迎窗口");
        }
        catch (Exception ex)
        {
            AppendOutput($"[error] 打开欢迎窗口失败: {ex.Message}");
        }
    }

    /// <summary>notify 命令：发送测试通知，用于验证通知卡片（InfoBar）的样式与动画</summary>
    private void ShowNotifyCommand(string[] args)
    {
        var notif = NavigationStore.MainWindow?.Notifications;
        if (notif == null)
        {
            AppendOutput("[error] 无法获取 NotificationService");
            return;
        }

        var type = args.Length > 0 ? args[0].ToLower() : "all";
        switch (type)
        {
            case "info":
                notif.Show("提示", "这是一条信息通知", NotificationType.Info);
                break;
            case "success":
                notif.Show("成功", "操作已成功完成", NotificationType.Success);
                break;
            case "warning":
                notif.Show("警告", "磁盘空间不足，请及时清理", NotificationType.Warning);
                break;
            case "error":
                notif.Show("错误", "下载失败，请检查网络后重试", NotificationType.Error);
                break;
            case "progress":
            {
                var id = notif.Show("下载中", "正在下载资源文件... 0%", NotificationType.Progress);
                double p = 0;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (_, _) =>
                {
                    p = Math.Min(100, p + 10);
                    notif.Update(id, $"正在下载资源文件... {p:0}%", p);
                    if (p >= 100) timer.Stop();
                };
                timer.Start();
                break;
            }
            case "countdown":
                notif.ShowCountdown("重启生效", "插件已安装，重启启动器后生效", 5);
                break;
            default:
                notif.Show("提示", "这是一条信息通知", NotificationType.Info);
                notif.Show("成功", "操作已成功完成", NotificationType.Success);
                notif.Show("警告", "磁盘空间不足，请及时清理", NotificationType.Warning);
                break;
        }

        AppendOutput($"[info] 已发送通知: {type}");
    }

    private void AppendOutput(string text)
    {
        Output += text + "\r\n";
    }

    [RelayCommand]
    private void Execute()
    {
        if (string.IsNullOrWhiteSpace(Command)) return;

        var parts = Command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLower();
        var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        AppendOutput($"> {Command}");

        if (_commands.TryGetValue(cmd, out var handler))
        {
            try
            {
                handler(args);
            }
            catch (Exception ex)
            {
                AppendOutput($"执行错误: {ex.Message}");
            }
        }
        else
        {
            AppendOutput($"未知命令: {cmd}");
        }

        Command = string.Empty;
    }

    [RelayCommand]
    private void Close()
    {
        _window.Close();
    }
}

file static class RuntimeInformation
{
    public static string FrameworkDescription => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
}
