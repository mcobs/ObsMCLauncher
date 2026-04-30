using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
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
    private readonly List<string> _logMessages = new();
    private bool _isGameRunning = true;

    private const int MaxLines = 5000;
    private const int TrimTo = 3500;

    private static readonly Regex AnsiEscapeRegex = new(@"\x1b\[(\d+(?:;\d+)*)m", RegexOptions.Compiled);
    private static readonly Regex LogLevelRegex = new(@"\/(INFO|WARN|WARNING|ERROR|SEVERE|FATAL|DEBUG|TRACE)\]?:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SectionSignRegex = new(@"\u00a7([0-9a-fA-FrR])", RegexOptions.Compiled);

    private static readonly Dictionary<char, string> SectionCodeToAnsi = new()
    {
        ['0'] = "\x1b[30m",
        ['1'] = "\x1b[34m",
        ['2'] = "\x1b[32m",
        ['3'] = "\x1b[36m",
        ['4'] = "\x1b[31m",
        ['5'] = "\x1b[35m",
        ['6'] = "\x1b[33m",
        ['7'] = "\x1b[90m",
        ['8'] = "\x1b[30;1m",
        ['9'] = "\x1b[94m",
        ['a'] = "\x1b[92m",
        ['b'] = "\x1b[96m",
        ['c'] = "\x1b[91m",
        ['d'] = "\x1b[95m",
        ['e'] = "\x1b[93m",
        ['f'] = "\x1b[97m",
        ['r'] = "\x1b[0m",
    };

    private static readonly Dictionary<int, string> DarkThemeAnsiColors = new()
    {
        [30] = "#676767",
        [31] = "#FF6B6B",
        [32] = "#6BFF6B",
        [33] = "#FFFF6B",
        [34] = "#6B6BFF",
        [35] = "#FF6BFF",
        [36] = "#6BFFFF",
        [37] = "#FFFFFF",
        [90] = "#909090",
        [91] = "#FF9090",
        [92] = "#90FF90",
        [93] = "#FFFF90",
        [94] = "#9090FF",
        [95] = "#FF90FF",
        [96] = "#90FFFF",
        [97] = "#FFFFFF",
    };

    private static readonly Dictionary<int, string> LightThemeAnsiColors = new()
    {
        [30] = "#000000",
        [31] = "#CC0000",
        [32] = "#008800",
        [33] = "#997700",
        [34] = "#0000CC",
        [35] = "#990099",
        [36] = "#008888",
        [37] = "#333333",
        [90] = "#666666",
        [91] = "#CC3333",
        [92] = "#338833",
        [93] = "#998833",
        [94] = "#3333CC",
        [95] = "#993399",
        [96] = "#338888",
        [97] = "#333333",
    };

    private bool IsLightTheme => Application.Current?.ActualThemeVariant == ThemeVariant.Light;
    private Dictionary<int, string> CurrentAnsiColors => IsLightTheme ? LightThemeAnsiColors : DarkThemeAnsiColors;

    public GameLogWindow()
    {
        InitializeComponent();

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    public GameLogWindow(string versionName) : this()
    {
        TitleText.Text = $"游戏日志 - {versionName}";

        AppendLog("游戏日志窗口已启动");
        AppendLog($"版本: {versionName}");
        AppendLog("等待游戏输出...");
        AppendLog("");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RebuildLog();
    }

    private void RebuildLog()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildLog);
            return;
        }

        if (LogTextBlock.Inlines == null) return;

        LogTextBlock.Inlines.Clear();
        foreach (var message in _logMessages)
        {
            AddColoredLine(message);
        }
    }

    public void AppendLog(string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(message));
            return;
        }

        _logMessages.Add(message);
        AddColoredLine(message);

        _lineCount++;
        LineCountText.Text = $"{_lineCount} 行";

        TrimLogIfNeeded();

        if (AutoScrollCheckBox.IsChecked == true)
        {
            LogScrollViewer.ScrollToEnd();
        }
    }

    private void AddColoredLine(string line)
    {
        if (LogTextBlock.Inlines == null) return;

        var hasAnsiCodes = line.Contains('\x1b');

        if (hasAnsiCodes)
        {
            AddAnsiColoredLine(line);
        }
        else
        {
            AddLogLevelColoredLine(line);
        }

        LogTextBlock.Inlines.Add(new LineBreak());
    }

    private void AddAnsiColoredLine(string line)
    {
        var colors = CurrentAnsiColors;
        var matches = AnsiEscapeRegex.Matches(line);
        var lastIndex = 0;
        var currentColor = -1;
        var isBold = false;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                var text = line.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrEmpty(text))
                {
                    var run = new Run(text);
                    if (currentColor >= 0 && colors.TryGetValue(currentColor, out var colorHex))
                    {
                        run.Foreground = new SolidColorBrush(Color.Parse(colorHex));
                    }
                    LogTextBlock.Inlines.Add(run);
                }
            }

            var codes = match.Groups[1].Value.Split(';');
            foreach (var codeStr in codes)
            {
                if (!int.TryParse(codeStr, out var code)) continue;

                switch (code)
                {
                    case 0:
                        currentColor = -1;
                        isBold = false;
                        break;
                    case 1:
                        isBold = true;
                        if (currentColor is >= 30 and <= 37)
                            currentColor += 60;
                        break;
                    case 22:
                        isBold = false;
                        if (currentColor is >= 90 and <= 97)
                            currentColor -= 60;
                        break;
                    case >= 30 and <= 37:
                        currentColor = isBold ? code + 60 : code;
                        break;
                    case >= 90 and <= 97:
                        currentColor = code;
                        break;
                }
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < line.Length)
        {
            var text = line.Substring(lastIndex);
            if (!string.IsNullOrEmpty(text))
            {
                var run = new Run(text);
                if (currentColor >= 0 && colors.TryGetValue(currentColor, out var colorHex))
                {
                    run.Foreground = new SolidColorBrush(Color.Parse(colorHex));
                }
                LogTextBlock.Inlines.Add(run);
            }
        }
    }

    private void AddLogLevelColoredLine(string line)
    {
        var logLevelMatch = LogLevelRegex.Match(line);
        if (!logLevelMatch.Success)
        {
            LogTextBlock.Inlines.Add(new Run(line));
            return;
        }

        var level = logLevelMatch.Groups[1].Value.ToUpperInvariant();
        var colorHex = GetLogLevelColor(level);

        var beforeText = line.Substring(0, logLevelMatch.Index);
        var levelText = logLevelMatch.Value;
        var afterText = line.Substring(logLevelMatch.Index + logLevelMatch.Length);

        if (!string.IsNullOrEmpty(beforeText))
        {
            LogTextBlock.Inlines.Add(new Run(beforeText));
        }

        var levelRun = new Run(levelText);
        if (colorHex != null)
        {
            levelRun.Foreground = new SolidColorBrush(Color.Parse(colorHex));
        }
        LogTextBlock.Inlines.Add(levelRun);

        if (!string.IsNullOrEmpty(afterText))
        {
            LogTextBlock.Inlines.Add(new Run(afterText));
        }
    }

    private string? GetLogLevelColor(string level)
    {
        var colors = CurrentAnsiColors;
        return level switch
        {
            "ERROR" or "SEVERE" or "FATAL" => colors[31],
            "WARN" or "WARNING" => colors[33],
            "DEBUG" or "TRACE" => colors[90],
            _ => null
        };
    }

    private void TrimLogIfNeeded()
    {
        if (_lineCount <= MaxLines) return;

        var removeCount = _lineCount - TrimTo;
        if (removeCount > 0 && removeCount < _logMessages.Count)
        {
            _logMessages.RemoveRange(0, removeCount);
        }
        else if (removeCount >= _logMessages.Count)
        {
            _logMessages.Clear();
        }

        _lineCount = _logMessages.Count;
        LineCountText.Text = $"{_lineCount} 行";

        RebuildLog();
    }

    public void AppendGameOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        var converted = ConvertSectionCodes(output);
        AppendLog(converted);
    }

    private static string ConvertSectionCodes(string text)
    {
        if (!text.Contains('\u00a7')) return text;

        return SectionSignRegex.Replace(text, match =>
        {
            var code = char.ToLowerInvariant(match.Groups[1].Value[0]);
            return SectionCodeToAnsi.TryGetValue(code, out var ansi) ? ansi : "";
        });
    }

    public void OnGameExit(int exitCode)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGameExit(exitCode));
            return;
        }

        _isGameRunning = false;
        StatusDot.Background = Brushes.Gray;
        StatusText.Text = $"游戏已退出 (代码: {exitCode})";

        AppendLog("");
        if (exitCode == 0)
            AppendLog($"游戏正常退出 (退出代码: {exitCode})");
        else
            AppendLog($"游戏异常退出 (退出代码: {exitCode})");
    }

    private void GameLogWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        }

        _isGameRunning = false;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
            {
                desktop.Shutdown();
            }
        }
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        if (LogTextBlock.Inlines != null)
        {
            LogTextBlock.Inlines.Clear();
        }
        _logMessages.Clear();
        _lineCount = 0;
        LineCountText.Text = "0 行";
        AppendLog("日志已清空");
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
                var cleanMessages = new List<string>(_logMessages.Count);
                foreach (var msg in _logMessages)
                {
                    cleanMessages.Add(StripAnsiCodes(msg));
                }
                await File.WriteAllLinesAsync(path, cleanMessages, Encoding.UTF8);
                AppendLog($"日志已保存到: {path}");
            }
            catch (Exception ex)
            {
                AppendLog($"保存日志失败: {ex.Message}");
            }
        }
    }

    private static string StripAnsiCodes(string text)
    {
        return AnsiEscapeRegex.Replace(text, "");
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
