using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace ObsMCLauncher.Windows
{
    public partial class GameLogWindow : Window
    {
        private int _lineCount = 0;

        public GameLogWindow(string versionName)
        {
            InitializeComponent();
            TitleText.Text = $"游戏日志 - {versionName}";
            
            // 初始化日志
            AppendLog("游戏日志窗口已启动", LogLevel.Info);
            AppendLog($"版本: {versionName}", LogLevel.Info);
            AppendLog("等待游戏输出...", LogLevel.Info);
            AppendLog("", LogLevel.Info);
        }

        /// <summary>
        /// 日志级别
        /// </summary>
        public enum LogLevel
        {
            Info,
            Warning,
            Error,
            Debug,
            Success
        }

        /// <summary>
        /// 追加日志
        /// </summary>
        public void AppendLog(string message, LogLevel level = LogLevel.Info)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message, level));
                return;
            }

            var paragraph = new Paragraph();
            var run = new Run(message);

            // 根据日志级别设置颜色（使用固定颜色，不受主题影响）
            run.Foreground = level switch
            {
                LogLevel.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)), // 红色
                LogLevel.Warning => new SolidColorBrush(Color.FromRgb(245, 158, 11)), // 橙色
                LogLevel.Success => new SolidColorBrush(Color.FromRgb(34, 197, 94)), // 绿色
                LogLevel.Debug => new SolidColorBrush(Color.FromRgb(156, 163, 175)), // 灰色
                _ => GetDynamicBrush("TextBrush") // 使用主题文本颜色
            };

            paragraph.Inlines.Add(run);
            LogTextBox.Document.Blocks.Add(paragraph);

            _lineCount++;
            LineCountText.Text = $"{_lineCount} 行";

            // 自动滚动到底部
            if (AutoScrollCheckBox.IsChecked == true)
            {
                LogTextBox.ScrollToEnd();
            }
        }

        /// <summary>
        /// 获取动态画刷资源
        /// </summary>
        private SolidColorBrush GetDynamicBrush(string resourceKey)
        {
            if (Application.Current.TryFindResource(resourceKey) is SolidColorBrush brush)
            {
                return brush;
            }
            // 默认返回白色（深色主题）或黑色（浅色主题）
            return new SolidColorBrush(Colors.White);
        }

        /// <summary>
        /// 解析游戏输出并添加到日志
        /// </summary>
        public void AppendGameOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return;

            // 检测日志级别
            var level = LogLevel.Info;
            var lowerOutput = output.ToLower();

            if (lowerOutput.Contains("error") || lowerOutput.Contains("exception") || 
                lowerOutput.Contains("fatal") || lowerOutput.Contains("crash"))
            {
                level = LogLevel.Error;
            }
            else if (lowerOutput.Contains("warn"))
            {
                level = LogLevel.Warning;
            }
            else if (lowerOutput.Contains("debug"))
            {
                level = LogLevel.Debug;
            }
            else if (lowerOutput.Contains("done") || lowerOutput.Contains("success") || 
                     lowerOutput.Contains("完成") || lowerOutput.Contains("成功"))
            {
                level = LogLevel.Success;
            }

            AppendLog(output, level);
        }

        /// <summary>
        /// 游戏退出时调用
        /// </summary>
        public void OnGameExit(int exitCode)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnGameExit(exitCode));
                return;
            }

            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)); // 灰色
            StatusText.Text = $"游戏已退出 (退出代码: {exitCode})";

            AppendLog("", LogLevel.Info);
            if (exitCode == 0)
            {
                AppendLog($"游戏正常退出 (退出代码: {exitCode})", LogLevel.Success);
            }
            else
            {
                AppendLog($"游戏异常退出 (退出代码: {exitCode})", LogLevel.Error);
            }
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Document.Blocks.Clear();
            _lineCount = 0;
            LineCountText.Text = "0 行";
            AppendLog("日志已清空", LogLevel.Info);
        }

        /// <summary>
        /// 保存日志
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                FileName = $"game_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                DefaultExt = "txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var textRange = new TextRange(
                        LogTextBox.Document.ContentStart,
                        LogTextBox.Document.ContentEnd
                    );

                    using (var fileStream = new FileStream(saveDialog.FileName, FileMode.Create))
                    {
                        textRange.Save(fileStream, DataFormats.Text);
                    }

                    AppendLog($"日志已保存到: {saveDialog.FileName}", LogLevel.Success);
                }
                catch (Exception ex)
                {
                    AppendLog($"保存日志失败: {ex.Message}", LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// 最小化窗口
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
