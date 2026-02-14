using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;

namespace ObsMCLauncher.Desktop.Views;

public partial class GameLogWindow : Window
{
    private void TitleBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private int _lineCount = 0;

    public GameLogWindow()
    {
        InitializeComponent();
    }

    public GameLogWindow(string versionName) : this()
    {
        TitleText.Text = $"游戏日志 - {versionName}";
        
        AppendLog("游戏日志窗口已启动", LogLevel.Info);
        AppendLog($"版本: {versionName}", LogLevel.Info);
        AppendLog("等待游戏输出...", LogLevel.Info);
        AppendLog("", LogLevel.Info);
    }

    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug,
        Success
    }

    public void AppendLog(string message, LogLevel level = LogLevel.Info)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(message, level));
            return;
        }

        LogTextBox.Text += (string.IsNullOrEmpty(LogTextBox.Text) ? "" : Environment.NewLine) + message;

        _lineCount++;
        LineCountText.Text = $"{_lineCount} 行";

        if (AutoScrollCheckBox.IsChecked == true)
        {
            LogTextBox.CaretIndex = LogTextBox.Text.Length;
            LogScrollViewer.ScrollToEnd();
        }
    }

    public void AppendGameOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

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

    public void OnGameExit(int exitCode)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameExit(exitCode));
            return;
        }

        StatusDot.Background = Brushes.Gray;
        StatusText.Text = $"游戏已退出 (代码: {exitCode})";

        AppendLog("", LogLevel.Info);
        if (exitCode == 0)
            AppendLog($"游戏正常退出 (退出代码: {exitCode})", LogLevel.Success);
        else
            AppendLog($"游戏异常退出 (退出代码: {exitCode})", LogLevel.Error);
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
        _lineCount = 0;
        LineCountText.Text = "0 行";
        AppendLog("日志已清空", LogLevel.Info);
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var storage = this.StorageProvider;
        if (storage == null) return;

        var options = new FilePickerSaveOptions
        {
            Title = "保存日志文件",
            SuggestedFileName = $"game_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("文本文件") { Patterns = new[] { "*.txt", "*.log" } }
            }
        };

        var file = await storage.SaveFilePickerAsync(options);
        if (file != null)
        {
            try
            {
                var path = file.Path.LocalPath;
                await File.WriteAllTextAsync(path, LogTextBox.Text ?? string.Empty, Encoding.UTF8);
                AppendLog($"日志已保存到: {path}", LogLevel.Success);
            }
            catch (Exception ex)
            {
                AppendLog($"保存日志失败: {ex.Message}", LogLevel.Error);
            }
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
