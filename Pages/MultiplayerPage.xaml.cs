using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Pages
{
    public partial class MultiplayerPage : Page
    {
        private readonly MciLmLinkService _linkService = new();

        public Action? OnBackRequested { get; set; }

        private bool _moduleReady;

        public MultiplayerPage()
        {
            InitializeComponent();
            Loaded += MultiplayerPage_Loaded;
            Unloaded += MultiplayerPage_Unloaded;

            // 监听子进程退出，避免“停止联机”按钮残留
            _linkService.ProcessExited += (s, exitCode) =>
            {
                Debug.WriteLine($"[Multiplayer] MciLm-link 进程退出，ExitCode={exitCode}");
                Dispatcher.Invoke(() =>
                {
                    UpdateProcessUi(isRunning: false);
                    NotificationManager.Instance.ShowNotification(
                        "联机已结束",
                        $"联机模块已退出（退出码 {exitCode}）",
                        NotificationType.Info,
                        4
                    );
                });
            };
        }

        private async void MultiplayerPage_Loaded(object sender, RoutedEventArgs e)
        {
            ShowModeSelectView();
            UpdateProcessUi(isRunning: false);
            await EnsureModuleReadyAsync(interactive: true);
        }

        private void MultiplayerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _linkService.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        private void UpdateProcessUi(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (StopLinkButton != null)
                {
                    StopLinkButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
                }
            });
        }

        private void ShowModeSelectView()
        {
            ModeSelectView.Visibility = Visibility.Visible;
            DetailView.Visibility = Visibility.Collapsed;
            TopBackButton.Visibility = Visibility.Collapsed;
        }

        private void ShowDetailView(string title, bool host)
        {
            ModeSelectView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Visible;
            TopBackButton.Visibility = Visibility.Visible;

            DetailTitleText.Text = title;
            HostPanel.Visibility = host ? Visibility.Visible : Visibility.Collapsed;
            JoinPanel.Visibility = host ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetModuleReadyUi(bool ready, string status)
        {
            _moduleReady = ready;
            Dispatcher.Invoke(() =>
            {
                ModuleStatusText.Text = status;
                OpenHostPanelButton.IsEnabled = ready;
                OpenJoinPanelButton.IsEnabled = ready;
            });
        }

        private async Task EnsureModuleReadyAsync(bool interactive)
        {
            try
            {
                Debug.WriteLine("[Multiplayer] 检测联机模块...");
                var exePath = _linkService.GetExecutablePath();
                Debug.WriteLine($"[Multiplayer] 目标路径: {exePath}");

                if (_linkService.IsInstalled())
                {
                    Debug.WriteLine("[Multiplayer] 模块已安装");
                    SetModuleReadyUi(true, "联机模块已安装，可使用");
                    return;
                }

                Debug.WriteLine("[Multiplayer] 模块未安装");

                if (!interactive)
                {
                    SetModuleReadyUi(false, "未安装联机模块");
                    return;
                }

                var allow = await DialogManager.Instance.ShowConfirmDialogAsync(
                    "需要下载联机模块",
                    "检测到未安装 MciLm-link 命令行组件。\n\n是否现在下载？",
                    "下载",
                    "取消"
                );

                Debug.WriteLine($"[Multiplayer] 用户选择下载: {allow}");

                if (!allow)
                {
                    SetModuleReadyUi(false, "未安装联机模块（已取消下载）");
                    return;
                }

                var notificationId = NotificationManager.Instance.ShowNotification(
                    "正在准备联机组件",
                    "正在下载 MciLm-link...",
                    NotificationType.Progress,
                    durationSeconds: null
                );

                try
                {
                    var ok = await _linkService.DownloadAndInstallAsync(new Progress<string>(msg =>
                    {
                        Debug.WriteLine($"[Multiplayer] 下载进度: {msg}");
                        NotificationManager.Instance.UpdateNotification(notificationId, msg);
                        AppendOutput(msg);
                    }));

                    NotificationManager.Instance.RemoveNotification(notificationId);

                    if (!ok)
                    {
                        Debug.WriteLine("[Multiplayer] 下载失败");
                        SetModuleReadyUi(false, "联机模块下载失败");
                        NotificationManager.Instance.ShowNotification(
                            "下载失败",
                            "MciLm-link 下载失败，请检查网络后重试",
                            NotificationType.Error,
                            5
                        );
                        return;
                    }

                    Debug.WriteLine("[Multiplayer] 下载成功");
                    NotificationManager.Instance.ShowNotification(
                        "准备完成",
                        "MciLm-link 已下载",
                        NotificationType.Success,
                        3
                    );

                    SetModuleReadyUi(true, "联机模块已安装，可使用");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Multiplayer] 下载异常: {ex.Message}");
                    NotificationManager.Instance.RemoveNotification(notificationId);
                    SetModuleReadyUi(false, "联机模块下载失败");
                    NotificationManager.Instance.ShowNotification(
                        "准备失败",
                        ex.Message,
                        NotificationType.Error,
                        5
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Multiplayer] 模块检测异常: {ex.Message}");
                SetModuleReadyUi(false, "联机模块检测失败");
            }
        }

        private void AppendOutput(string line)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(OutputTextBox.Text))
                {
                    OutputTextBox.Text = line;
                }
                else
                {
                    OutputTextBox.Text += "\n" + line;
                }
                OutputTextBox.ScrollToEnd();
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
                        var code = m.Groups["code"].Value;
                        Dispatcher.Invoke(() =>
                        {
                            ShareCodeTextBox.Text = code;
                            CopyShareCodeButton.IsEnabled = !string.IsNullOrWhiteSpace(code);
                        });
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private void OpenHostPanelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_moduleReady)
                return;

            ShowDetailView("我要开房", host: true);
        }

        private void OpenJoinPanelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_moduleReady)
                return;

            ShowDetailView("加入房间", host: false);
        }

        private void TopBackButton_Click(object sender, RoutedEventArgs e)
        {
            ShowModeSelectView();
        }

        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            await EnsureModuleReadyAsync(interactive: true);
            if (!_moduleReady) return;

            if (!int.TryParse(ServerPortTextBox.Text?.Trim(), out var port) || port <= 0 || port > 65535)
            {
                NotificationManager.Instance.ShowNotification(
                    "端口无效",
                    "请输入 1-65535 之间的端口",
                    NotificationType.Warning,
                    3
                );
                return;
            }

            ShareCodeTextBox.Text = string.Empty;
            CopyShareCodeButton.IsEnabled = false;

            AppendOutput($"启动服务提供者模式: {port}");
            Debug.WriteLine($"[Multiplayer] 启动 server 模式: {port}");

            var ok = _linkService.StartServer(port, line =>
            {
                AppendOutput(line);
                TryParseShareCode(line);
            });

            if (!ok)
            {
                UpdateProcessUi(isRunning: false);
                NotificationManager.Instance.ShowNotification(
                    "启动失败",
                    "无法启动 MciLm-link",
                    NotificationType.Error,
                    5
                );
            }
            else
            {
                UpdateProcessUi(isRunning: true);
                NotificationManager.Instance.ShowNotification(
                    "已启动",
                    "正在生成分享码...",
                    NotificationType.Success,
                    3
                );
            }
        }

        private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
        {
            await EnsureModuleReadyAsync(interactive: true);
            if (!_moduleReady) return;

            var code = JoinCodeTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                NotificationManager.Instance.ShowNotification(
                    "请输入分享码",
                    "分享码不能为空",
                    NotificationType.Warning,
                    3
                );
                return;
            }

            AppendOutput($"启动客户端模式: {code}");
            Debug.WriteLine($"[Multiplayer] 启动 client 模式: {code}");

            var ok = _linkService.JoinServer(code, line =>
            {
                AppendOutput(line);
            });

            if (!ok)
            {
                UpdateProcessUi(isRunning: false);
                NotificationManager.Instance.ShowNotification(
                    "启动失败",
                    "无法启动 MciLm-link",
                    NotificationType.Error,
                    5
                );
            }
            else
            {
                UpdateProcessUi(isRunning: true);
                NotificationManager.Instance.ShowNotification(
                    "已启动",
                    "联机连接中...",
                    NotificationType.Success,
                    3
                );
            }
        }

        private async void StopLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirm = await DialogManager.Instance.ShowConfirmDialogAsync(
                    "停止联机",
                    "确定要停止联机吗？\n\n停止后，当前联机会断开。",
                    "停止联机",
                    "取消"
                );

                if (!confirm)
                {
                    return;
                }

                Debug.WriteLine("[Multiplayer] 停止联机");
                _linkService.Stop();
                UpdateProcessUi(isRunning: false);

                NotificationManager.Instance.ShowNotification(
                    "已停止",
                    "联机进程已停止",
                    NotificationType.Success,
                    2
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Multiplayer] 停止联机失败: {ex.Message}");
                NotificationManager.Instance.ShowNotification(
                    "停止失败",
                    ex.Message,
                    NotificationType.Error,
                    4
                );
            }
        }

        private void CopyShareCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var code = ShareCodeTextBox.Text;
            if (string.IsNullOrWhiteSpace(code)) return;

            Clipboard.SetText(code);
            NotificationManager.Instance.ShowNotification(
                "已复制",
                "分享码已复制到剪贴板",
                NotificationType.Success,
                2
            );
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            OnBackRequested?.Invoke();
        }
    }
}
