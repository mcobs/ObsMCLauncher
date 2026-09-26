using System;
using System.Collections.Concurrent;
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
using ObsMCLauncher.Desktop.Services;

namespace ObsMCLauncher.Desktop.Views;

public partial class GameLogWindow : Window
{
    private int _lineCount = 0;
    private readonly List<string> _logMessages = [];

    private const int MaxLines = 5000;
    private const int TrimTo = 3500;

    // ===== 高吞吐日志的防卡死 =====
    // 游戏（尤其带一堆 Mod 时）一秒能吐几千行。以前每行都 Dispatcher.Post 一次、
    // 且超行后每行都触发一次全量 RebuildLog（O(n) 个 Run），UI 线程直接被灌满 → 软件假死。
    // 现在：后台线程只入队，UI 线程按固定节奏**批量**消费一次。
    private readonly ConcurrentQueue<string> _pendingLines = new();
    private readonly DispatcherTimer _flushTimer;
    private int _droppedLines;

    /// <summary>刷新间隔（毫秒）—— 一秒内最多刷 ~8 次，肉眼看着仍是"实时"的。</summary>
    private const int FlushIntervalMs = 120;

    /// <summary>单次刷新最多处理多少行，避免一次卡顿太久。</summary>
    private const int MaxLinesPerFlush = 600;

    /// <summary>待处理队列上限，超出后丢最旧的（防内存无上限增长）。</summary>
    private const int MaxPendingLines = 20000;

    [GeneratedRegex(@"\x1b\[(\d+(?:;\d+)*)m")]
    private static partial Regex AnsiEscapeRegex();
    [GeneratedRegex(@"\/(INFO|WARN|WARNING|ERROR|SEVERE|FATAL|DEBUG|TRACE)\]?:", RegexOptions.IgnoreCase)]
    private static partial Regex LogLevelRegex();
    [GeneratedRegex(@"\u00a7([0-9a-fA-FrR])")]
    private static partial Regex SectionSignRegex();

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

    private static bool IsLightTheme => Application.Current?.ActualThemeVariant == ThemeVariant.Light;
    private Dictionary<int, string> CurrentAnsiColors => IsLightTheme ? LightThemeAnsiColors : DarkThemeAnsiColors;

    public GameLogWindow()
    {
        InitializeComponent();

        WindowChrome.Apply(this);

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(FlushIntervalMs)
        };
        _flushTimer.Tick += (_, _) => FlushPendingLines();

        Closed += (_, _) =>
        {
            _flushTimer.Stop();
            if (Application.Current != null)
            {
                Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
            }
        };

        _flushTimer.Start();
    }

    public GameLogWindow(string versionName) : this()
    {
        Title = $"游戏日志 - {versionName}";

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
        var matches = AnsiEscapeRegex().Matches(line);
        var lastIndex = 0;
        var currentColor = -1;
        var isBold = false;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                var text = line[lastIndex..match.Index];
                if (!string.IsNullOrEmpty(text))
                {
                    var run = new Run(text);
                    if (currentColor >= 0 && colors.TryGetValue(currentColor, out var colorHex))
                    {
                        run.Foreground = new SolidColorBrush(Color.Parse(colorHex));
                    }
                    LogTextBlock.Inlines!.Add(run);
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
            var text = line[lastIndex..];
            if (!string.IsNullOrEmpty(text))
            {
                var run = new Run(text);
                if (currentColor >= 0 && colors.TryGetValue(currentColor, out var colorHex))
                {
                    run.Foreground = new SolidColorBrush(Color.Parse(colorHex));
                }
                LogTextBlock.Inlines!.Add(run);
            }
        }
    }

    private void AddLogLevelColoredLine(string line)
    {
        var logLevelMatch = LogLevelRegex().Match(line);
        if (!logLevelMatch.Success)
        {
            LogTextBlock.Inlines!.Add(new Run(line));
            return;
        }

        var level = logLevelMatch.Groups[1].Value.ToUpperInvariant();
        var colorHex = GetLogLevelColor(level);

        var beforeText = line[..logLevelMatch.Index];
        var levelText = logLevelMatch.Value;
        var afterText = line[(logLevelMatch.Index + logLevelMatch.Length)..];

        if (!string.IsNullOrEmpty(beforeText))
        {
            LogTextBlock.Inlines!.Add(new Run(beforeText));
        }

        var levelRun = new Run(levelText);
        if (colorHex != null)
        {
            levelRun.Foreground = new SolidColorBrush(Color.Parse(colorHex));
        }
        LogTextBlock.Inlines!.Add(levelRun);

        if (!string.IsNullOrEmpty(afterText))
        {
            LogTextBlock.Inlines!.Add(new Run(afterText));
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
        // 多留一段余量再裁：裁剪要全量重建 Inlines（O(n) 个 Run），
        // 每次刷新都裁一次会把 UI 线程钉死，攒够一批再裁划算得多。
        if (_lineCount <= MaxLines + 1000) return;

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

    /// <summary>
    /// 由游戏进程的输出线程调用（可能每毫秒一次）：<b>只入队，不碰 UI</b>。
    /// 真正往界面上加由 <see cref="FlushPendingLines"/> 在 UI 线程批量完成。
    /// </summary>
    public void AppendGameOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        // 颜色码转换留在调用线程做，别占 UI 线程
        _pendingLines.Enqueue(ConvertSectionCodes(output));

        if (_pendingLines.Count > MaxPendingLines)
        {
            // UI 实在跟不上时丢最旧的，并记一笔，避免无上限吃内存
            while (_pendingLines.Count > MaxPendingLines && _pendingLines.TryDequeue(out _))
            {
                _droppedLines++;
            }
        }
    }

    /// <summary>
    /// 立刻把队列里剩下的行全部刷到界面（退出、导出、清空前都要先调一次，
    /// 否则缓冲区里的最后几行会丢）。
    /// </summary>
    public void FlushPending()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(FlushPending);
            return;
        }

        // 与定时刷新不同：这里不限量，保证剩余内容一次性落地
        var batch = new List<string>();
        while (_pendingLines.TryDequeue(out var line))
        {
            batch.Add(line);
            if (batch.Count >= MaxPendingLines) break;
        }

        if (_droppedLines > 0)
        {
            batch.Add($"… 界面刷新跟不上，已省略 {_droppedLines} 行日志");
            _droppedLines = 0;
        }

        AppendLogBatch(batch);
    }

    /// <summary>把队列里的行批量刷到界面（在 UI 线程上，由定时器驱动）。</summary>
    private void FlushPendingLines()
    {
        if (_pendingLines.IsEmpty)
            return;

        var batch = new List<string>();
        while (batch.Count < MaxLinesPerFlush && _pendingLines.TryDequeue(out var line))
        {
            batch.Add(line);
        }

        if (_droppedLines > 0)
        {
            batch.Add($"… 界面刷新跟不上，已省略 {_droppedLines} 行日志");
            _droppedLines = 0;
        }

        AppendLogBatch(batch);
    }

    /// <summary>批量追加：一次刷新只做一次计数更新、一次裁剪、一次滚动。</summary>
    private void AppendLogBatch(IReadOnlyList<string> messages)
    {
        if (messages.Count == 0) return;

        foreach (var message in messages)
        {
            _logMessages.Add(message);
            AddColoredLine(message);
            _lineCount++;
        }

        LineCountText.Text = $"{_lineCount} 行";

        TrimLogIfNeeded();

        if (AutoScrollCheckBox.IsChecked == true)
        {
            LogScrollViewer.ScrollToEnd();
        }
    }

    private static string ConvertSectionCodes(string text)
    {
        if (!text.Contains('\u00a7')) return text;

        return SectionSignRegex().Replace(text, match =>
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

        StatusDot.Background = Brushes.Gray;
        StatusText.Text = $"游戏已退出 (代码: {exitCode})";

        // 先把缓冲区里剩下的行落地，再写"已退出"，顺序才对
        FlushPending();

        AppendLog("");
        if (exitCode == 0)
            AppendLog($"游戏正常退出 (退出代码: {exitCode})");
        else
            AppendLog($"游戏异常退出 (退出代码: {exitCode})");
    }

    private bool _isShuttingDown;

    private void GameLogWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isShuttingDown) return;

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == null || !desktop.MainWindow.IsVisible)
            {
                _isShuttingDown = true;
                desktop.Shutdown();
            }
        }
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        if (LogTextBlock.Inlines is not null)
        {
            LogTextBlock.Inlines.Clear();
        }
        // 待处理队列也要清：否则清空后下一拍又把旧行刷回来
        while (_pendingLines.TryDequeue(out _) ) { }
        _droppedLines = 0;
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
                new FilePickerFileType("文本文件") { Patterns = ["*.txt", "*.log"] }
            }
        };

        var file = await storage.SaveFilePickerAsync(options);
        if (file != null)
        {
            try
            {
                // 导出前把缓冲区刷干净，否则最后一段日志不在文件里
                FlushPending();

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
        return AnsiEscapeRegex().Replace(text, "");
    }
}
