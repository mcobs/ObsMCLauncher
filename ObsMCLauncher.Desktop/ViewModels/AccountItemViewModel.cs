using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 账号列表项的视图模型包装：承载每账号独立的交互状态（刷新中/操作中），
/// 避免全局单一状态导致刷新一个账号时所有账号的按钮都被禁用。
/// </summary>
public partial class AccountItemViewModel : ObservableObject
{
    [ObservableProperty]
    private GameAccount _account;

    /// <summary>该账号刷新中（刷新按钮显示转圈并禁用）</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>该账号操作中（删除等异步操作期间禁用操作按钮，防止重复点击）</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>刷新按钮可用性（刷新中或操作中不可用）</summary>
    public bool CanRefresh => !IsRefreshing && !IsBusy;

    /// <summary>删除按钮可用性（操作中不可用）</summary>
    public bool CanDelete => !IsBusy;

    public AccountItemViewModel(GameAccount account)
    {
        _account = account;
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanDelete));
    }
}
