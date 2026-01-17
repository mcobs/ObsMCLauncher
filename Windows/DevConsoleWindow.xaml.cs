using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Windows
{
    public partial class DevConsoleWindow : Window
    {
        public DevConsoleWindow()
        {
            InitializeComponent();

            AppendLine("ObsMCLauncher " + VersionInfo.Version + " " + VersionInfo.VersionStatusText + " 开发者控制台");
            AppendLine("输入 help 查看可用命令");
            AppendLine(string.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            RunCurrentCommand();
        }

        private void CommandTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                RunCurrentCommand();
            }
        }

        private void RunCurrentCommand()
        {
            var cmd = (CommandTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cmd))
                return;

            CommandTextBox.Text = string.Empty;

            AppendLine($"> {cmd}");

            try
            {
                ExecuteCommand(cmd);
            }
            catch (Exception ex)
            {
                AppendLine($"[error] {ex.Message}");
            }
        }

        private void ExecuteCommand(string commandLine)
        {
            var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            var cmd = parts[0].ToLowerInvariant();
            var args = parts.Skip(1).ToArray();

            switch (cmd)
            {
                case "help":
                case "?":
                    AppendLine("可用命令:");
                    AppendLine("  help                 显示帮助");
                    AppendLine("  clear                清空输出");
                    AppendLine("  crash                直接打开崩溃窗口（不抛未处理异常）");
                    AppendLine("  throw <msg>          抛出一个未处理异常（msg 可选）");
                    AppendLine("  update [tag]         强制打开更新窗口（不依赖联网检查）；tag 可选，默认 v9.9.9-test");
                    break;

                case "clear":
                    OutputTextBox.Clear();
                    break;

                case "crash":
                    try
                    {
                        var summary = "手动打开崩溃窗口（crash 指令）";
                        var report = string.Join(Environment.NewLine, new[]
                        {
                            "========== ObsMCLauncher 崩溃报告 ==========",
                            $"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                            "标题: 手动打开崩溃窗口（crash 指令）",
                            $"版本: {VersionInfo.DisplayVersion}",
                            $"系统: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
                            $"架构: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
                            $"运行目录: {AppDomain.CurrentDomain.BaseDirectory}",
                            "",
                            "---------- 异常信息 ----------",
                            "(crash 指令不会抛出未处理异常；此窗口用于预览/验证导出与复制功能)"
                        });

                        App.ShowCrashWindow(summary, report);
                        AppendLine("[info] 已打开崩溃窗口");
                    }
                    catch (Exception ex)
                    {
                        AppendLine($"[error] 打开崩溃窗口失败: {ex.Message}");
                    }
                    break;

                case "throw":
                    {
                        var msg = args.Length > 0 ? string.Join(' ', args) : "手动抛出异常";
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            throw new Exception(msg);
                        }));
                        AppendLine("[info] 已触发 throw（异常将由全局捕获处理）");
                        break;
                    }

                case "update":
                    {
                        var tag = args.Length > 0 ? args[0] : "v9.9.9-test";
                        _ = OpenUpdateAsync(tag);
                        break;
                    }

                default:
                    AppendLine($"[warn] 未知命令: {cmd}（输入 help 查看帮助）");
                    break;
            }
        }

        private async System.Threading.Tasks.Task OpenUpdateAsync(string tagName)
        {
            try
            {
                AppendLine($"[info] 强制打开更新窗口: {tagName}");

                var release = new GitHubRelease
                {
                    TagName = tagName,
                    Name = $"测试更新 {tagName}",
                    Body = "**这是一个本地注入的测试更新**\n\n用于验证更新弹窗与下载流程。\n",
                    HtmlUrl = "https://example.com/",
                    PublishedAt = DateTime.Now,
                    Prerelease = true,
                    Assets = Array.Empty<GitHubAsset>()
                };

                await UpdateService.ShowUpdateDialogAsync(release);
            }
            catch (Exception ex)
            {
                AppendLine($"[error] 打开更新窗口失败: {ex.Message}");
            }
        }

        private void AppendLine(string line)
        {
            OutputTextBox.AppendText(line + Environment.NewLine);
            OutputTextBox.ScrollToEnd();
        }
    }
}
