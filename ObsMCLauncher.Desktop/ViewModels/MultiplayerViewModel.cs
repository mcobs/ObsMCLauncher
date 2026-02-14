using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Desktop.ViewModels.Notifications;
using ObsMCLauncher.Desktop.ViewModels.Dialogs;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class MultiplayerViewModel : ViewModelBase
{
    private readonly MciLmLinkService _linkService = new();
    private readonly NotificationService _notificationService;
    private readonly DialogService _dialogService;

    private bool _isInDetailView;
    private bool _isHostMode;
    private bool _isModuleReady;
    private bool _isRunning;
    private string _statusText = "正在检测联机模块...";
    private string _detailTitle = string.Empty;
    private string _serverPort = "25565";
    private string _shareCode = string.Empty;
    private string _joinCode = string.Empty;
    private string _outputLog = string.Empty;

    public bool IsInDetailView { get => _isInDetailView; set => SetProperty(ref _isInDetailView, value); }
    public bool IsHostMode { get => _isHostMode; set => SetProperty(ref _isHostMode, value); }
    public bool IsModuleReady { get => _isModuleReady; set => SetProperty(ref _isModuleReady, value); }
    public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string DetailTitle { get => _detailTitle; set => SetProperty(ref _detailTitle, value); }
    public string ServerPort { get => _serverPort; set => SetProperty(ref _serverPort, value); }
    public string ShareCode { get => _shareCode; set => SetProperty(ref _shareCode, value); }
    public string JoinCode { get => _joinCode; set => SetProperty(ref _joinCode, value); }
    public string OutputLog { get => _outputLog; set => SetProperty(ref _outputLog, value); }
    public bool HasShareCode => !string.IsNullOrWhiteSpace(ShareCode);

    public MultiplayerViewModel(NotificationService notificationService, DialogService dialogService)
    {
        _notificationService = notificationService;
        _dialogService = dialogService;

        _linkService.ProcessExited += (s, exitCode) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsRunning = false;
                _notificationService.Show("联机已结束", $"联机模块已退出（退出码 {exitCode}）", NotificationType.Info);
            });
        };

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await EnsureModuleReadyAsync(interactive: false);
    }

    private async Task EnsureModuleReadyAsync(bool interactive)
    {
        if (_linkService.IsInstalled())
        {
            IsModuleReady = true;
            StatusText = "联机模块已安装，可使用";
            return;
        }

        if (!interactive)
        {
            StatusText = "未安装联机模块";
            return;
        }

        var confirm = (await _dialogService.ShowQuestion("需要下载联机模块", "检测到未安装 MciLm-link 命令行组件。\n\n是否现在下载？")) == DialogResult.Yes;
        if (!confirm)
            {
            StatusText = "未安装联机模块（已取消下载）";
            return;
        }

        var notificationId = _notificationService.Show("正在准备联机组件", "正在下载 MciLm-link...", NotificationType.Progress);

        try
        {
            var ok = await _linkService.DownloadAndInstallAsync(new Progress<string>(msg =>
            {
                _notificationService.Update(notificationId, msg);
                AppendOutput(msg);
            }));

            _notificationService.Remove(notificationId);

            if (ok)
            {
                IsModuleReady = true;
                StatusText = "联机模块已安装，可使用";
                _notificationService.Show("准备完成", "MciLm-link 已下载", NotificationType.Success);
            }
            else
            {
                StatusText = "联机模块下载失败";
                _notificationService.Show("下载失败", "MciLm-link 下载失败，请检查网络后重试", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Remove(notificationId);
            StatusText = "联机模块下载失败";
            _notificationService.Show("准备失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void OpenHostPanel()
    {
        IsInDetailView = true;
        IsHostMode = true;
        DetailTitle = "我要开房";
    }

    [RelayCommand]
    private void OpenJoinPanel()
    {
        IsInDetailView = true;
        IsHostMode = false;
        DetailTitle = "加入房间";
    }

    [RelayCommand]
    private void BackToModeSelect()
    {
        IsInDetailView = false;
    }

    [RelayCommand]
    private async Task StartServer()
    {
        await EnsureModuleReadyAsync(interactive: true);
        if (!IsModuleReady) return;

        if (!int.TryParse(ServerPort?.Trim(), out var port) || port <= 0 || port > 65535)
        {
            _notificationService.Show("端口无效", "请输入 1-65535 之间的端口", NotificationType.Warning);
            return;
        }

        ShareCode = string.Empty;
        AppendOutput($"启动服务提供者模式: {port}");

        var ok = _linkService.StartServer(port, line =>
        {
            AppendOutput(line);
            TryParseShareCode(line);
        });

        if (ok)
        {
            IsRunning = true;
            _notificationService.Show("已启动", "正在生成分享码...", NotificationType.Success);
        }
        else
        {
            _notificationService.Show("启动失败", "无法启动 MciLm-link", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task JoinRoom()
    {
        await EnsureModuleReadyAsync(interactive: true);
        if (!IsModuleReady) return;

        if (string.IsNullOrWhiteSpace(JoinCode))
        {
            _notificationService.Show("请输入分享码", "分享码不能为空", NotificationType.Warning);
            return;
        }

        AppendOutput($"启动客户端模式: {JoinCode}");

        var ok = _linkService.JoinServer(JoinCode, line =>
        {
            AppendOutput(line);
        });

        if (ok)
        {
            IsRunning = true;
            _notificationService.Show("已启动", "联机连接中...", NotificationType.Success);
        }
        else
        {
            _notificationService.Show("启动失败", "无法启动 MciLm-link", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task StopLink()
    {
        var confirm = (await _dialogService.ShowQuestion("停止联机", "确定要停止联机吗？\n\n停止后，当前联机会断开。")) == DialogResult.Yes;
        if (!confirm) return;

        _linkService.Stop();
        IsRunning = false;
        _notificationService.Show("已停止", "联机进程已停止", NotificationType.Success);
    }

    [RelayCommand]
    private void CopyShareCode()
    {
        if (string.IsNullOrWhiteSpace(ShareCode)) return;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow?.Clipboard != null)
                {
                    await desktop.MainWindow.Clipboard.SetTextAsync(ShareCode);
            }
        }
            catch
            {
            }
        });
        _notificationService.Show("已复制", "分享码已复制到剪贴板", NotificationType.Success);
    }

    private void AppendOutput(string line)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            OutputLog += (string.IsNullOrEmpty(OutputLog) ? "" : "\n") + line;
        });
    }

    private void TryParseShareCode(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("msg", out var msgEl))
            {
                var msg = msgEl.GetString() ?? string.Empty;
                var m = Regex.Match(msg, @"分享码：(?<code>[A-Z0-9-]+)");
                if (m.Success)
                {
                    ShareCode = m.Groups["code"].Value;
                }
            }
        }
        catch { }
    }
}
