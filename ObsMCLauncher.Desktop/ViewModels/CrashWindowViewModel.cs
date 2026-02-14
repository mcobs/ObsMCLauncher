using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class CrashWindowViewModel : ObservableObject
{
    private readonly Window _window;
    private readonly string _crashReport;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _crashReportText = string.Empty;

    [ObservableProperty]
    private string _logPathText = "不会自动保存日志，你可以点击右侧按钮导出。";

    public string CrashReport
    {
        get => CrashReportText;
        set => SetProperty(ref _crashReportText, value);
    }

    public CrashWindowViewModel(string summary, string crashReport, Window window)
    {
        _window = window;
        _summary = summary;
        _crashReport = crashReport;
        _crashReportText = crashReport;
    }

    [RelayCommand]
    private void Close()
    {
        _window.Close();
    }

    [RelayCommand]
    private async Task Export()
    {
        var storageProvider = _window.StorageProvider;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出崩溃日志",
            SuggestedFileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("日志文件") { Patterns = new[] { "*.log" } },
                new FilePickerFileType("文本文件") { Patterns = new[] { "*.txt" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
            }
        });

        if (file != null)
        {
            try
            {
                await File.WriteAllTextAsync(file.Path.AbsolutePath, _crashReport ?? string.Empty);
                LogPathText = $"已导出到：{file.Path.LocalPath}";
            }
            catch (Exception ex)
            {
                LogPathText = $"导出失败：{ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task Copy()
    {
        try
        {
            var clipboard = _window.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(_crashReport ?? string.Empty);
            }
        }
        catch { }
    }

    [RelayCommand]
    private void Exit()
    {
        try
        {
            Environment.Exit(-1);
        }
        catch
        {
            _window.Close();
        }
    }
}
