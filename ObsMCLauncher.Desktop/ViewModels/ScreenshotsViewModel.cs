using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class ScreenshotsViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;

    [ObservableProperty]
    private ObservableCollection<ScreenshotInfo> _screenshots = new();

    [ObservableProperty]
    private ObservableCollection<string> _versions = new();

    [ObservableProperty]
    private string? _selectedVersion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isLoadingMore;

    private const int PAGE_SIZE = 20;
    private const int FILTER_DEBOUNCE_MS = 300;
    private int _currentPage = 0;
    private List<ScreenshotInfo>? _allFilteredScreenshots;
    private CancellationTokenSource? _filterCts;

    public ScreenshotsViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            var config = LauncherConfig.Load();

            await Task.Run(() =>
            {
                var versions = ScreenshotManager.Instance.GetVersionsWithScreenshots(config.GameDirectory);
                
                Dispatcher.UIThread.Post(() =>
                {
                    Versions.Clear();
                    foreach (var v in versions) Versions.Add(v);
                    SelectedVersion = Versions.FirstOrDefault();
                    
                    if (Versions.Count == 0)
                    {
                        IsEmpty = true;
                    }
                });
            });
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"加载截图列表失败: {ex.Message}", NotificationType.Error);
            IsEmpty = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    partial void OnSelectedVersionChanged(string? value)
    {
        _currentPage = 0;
        _ = FilterAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _currentPage = 0;
        _ = FilterAsync();
    }

    partial void OnStartDateChanged(DateTime? value)
    {
        _currentPage = 0;
        _ = FilterAsync();
    }

    partial void OnEndDateChanged(DateTime? value)
    {
        _currentPage = 0;
        _ = FilterAsync();
    }

    private async Task FilterAsync()
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        var token = cts.Token;

        try
        {
            await Task.Delay(FILTER_DEBOUNCE_MS, token);

            var config = LauncherConfig.Load();
            string? versionName = null;

            if (SelectedVersion != null && SelectedVersion != "全部" && SelectedVersion != "主目录")
            {
                versionName = SelectedVersion;
            }

            await Task.Run(() =>
            {
                var filtered = ScreenshotManager.Instance.GetScreenshots(config.GameDirectory, versionName);

                // 选择"主目录"时只显示主目录的截图
                if (SelectedVersion == "主目录")
                {
                    filtered = filtered.Where(s => s.VersionName == "主目录").ToList();
                }

                if (StartDate.HasValue || EndDate.HasValue)
                {
                    filtered = ScreenshotManager.Instance.FilterByDate(filtered, StartDate, EndDate);
                }

                if (!string.IsNullOrEmpty(SearchText))
                {
                    filtered = filtered
                        .Where(s => s.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                _allFilteredScreenshots = filtered.OrderByDescending(s => s.CreatedTime).ToList();

                var pageData = _allFilteredScreenshots
                    .Skip(_currentPage * PAGE_SIZE)
                    .Take(PAGE_SIZE)
                    .ToList();

                if (token.IsCancellationRequested) return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;

                    if (_currentPage == 0)
                    {
                        Screenshots.Clear();
                    }

                    foreach (var s in pageData) Screenshots.Add(s);
                    IsEmpty = Screenshots.Count == 0;
                });
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            _notificationService.Show("错误", $"筛选截图失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private Task LoadMoreAsync()
    {
        if (_allFilteredScreenshots == null || IsLoadingMore) return Task.CompletedTask;

        var totalLoaded = Screenshots.Count;
        if (totalLoaded >= _allFilteredScreenshots!.Count) return Task.CompletedTask;

        try
        {
            IsLoadingMore = true;
            _currentPage++;

            var pageData = _allFilteredScreenshots
                .Skip(_currentPage * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .ToList();

            foreach (var s in pageData) Screenshots.Add(s);
        }
        finally
        {
            IsLoadingMore = false;
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ViewScreenshot(ScreenshotInfo? screenshot)
    {
        if (screenshot == null) return;

        try
        {
            if (System.IO.File.Exists(screenshot.FullPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = screenshot.FullPath,
                    UseShellExecute = true
                });
            }
            else
            {
                _notificationService.Show("错误", "截图文件不存在", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"打开截图失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task ExportScreenshotAsync(ScreenshotInfo? screenshot)
    {
        if (screenshot == null) return;

        try
        {
            if (!File.Exists(screenshot.FullPath))
            {
                _notificationService.Show("错误", "截图文件不存在", NotificationType.Error);
                return;
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            var storage = desktop.MainWindow?.StorageProvider;
            if (storage == null) return;

            var extension = Path.GetExtension(screenshot.FullPath);
            if (string.IsNullOrEmpty(extension)) extension = ".png";

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出截图",
                SuggestedFileName = Path.GetFileName(screenshot.FullPath),
                DefaultExtension = extension.TrimStart('.'),
                FileTypeChoices = new[] { new FilePickerFileType("图片文件") { Patterns = new[] { $"*{extension}" } } }
            });

            if (file == null) return;

            await using var source = File.OpenRead(screenshot.FullPath);
            await using var destination = await file.OpenWriteAsync();
            await source.CopyToAsync(destination);

            _notificationService.Show("导出成功", $"截图已导出到 {file.Name}", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.Show("导出失败", $"导出截图失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private Task DeleteScreenshotAsync(ScreenshotInfo? screenshot)
    {
        if (screenshot == null) return Task.CompletedTask;

        try
        {
            if (ScreenshotManager.Instance.DeleteScreenshot(screenshot.FullPath))
            {
                _notificationService.Show("成功", "截图已删除", NotificationType.Success);
                
                Screenshots.Remove(screenshot);
                IsEmpty = Screenshots.Count == 0;
            }
            else
            {
                _notificationService.Show("错误", "删除截图失败", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"删除截图失败: {ex.Message}", NotificationType.Error);
        }

        return Task.CompletedTask;
    }
}
