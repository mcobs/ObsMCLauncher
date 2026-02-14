using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
    private int _currentPage = 0;
    private List<ScreenshotInfo>? _allFilteredScreenshots;

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
                });
            });
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"加载截图列表失败: {ex.Message}", NotificationType.Error);
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
        try
        {
            var config = LauncherConfig.Load();
            string? versionName = null;
            
            if (SelectedVersion != null && SelectedVersion != "全部")
            {
                versionName = SelectedVersion;
            }

            await Task.Run(() =>
            {
                var filtered = ScreenshotManager.Instance.GetScreenshots(config.GameDirectory, versionName);

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

                Dispatcher.UIThread.Post(() =>
                {
                    if (_currentPage == 0)
                    {
                        Screenshots.Clear();
                    }
                    
                    foreach (var s in pageData) Screenshots.Add(s);
                    IsEmpty = Screenshots.Count == 0;
                });
            });
        }
        catch (Exception ex)
        {
            _notificationService.Show("错误", $"筛选截图失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_allFilteredScreenshots == null || IsLoadingMore) return;

        var totalLoaded = Screenshots.Count;
        if (totalLoaded >= _allFilteredScreenshots!.Count) return;

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

        _notificationService.Show("提示", "导出功能需要选择目录，暂未实现文件对话框", NotificationType.Info);
    }

    [RelayCommand]
    private async Task DeleteScreenshotAsync(ScreenshotInfo? screenshot)
    {
        if (screenshot == null) return;

        try
        {
            if (ScreenshotManager.Instance.DeleteScreenshot(screenshot.FullPath))
            {
                _notificationService.Show("成功", "截图已删除", NotificationType.Success);
                
                // 从列表中移除
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
    }
}
