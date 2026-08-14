using System;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 账号变更事件：解耦账号管理页与主页，
/// 不再通过导航项标题（"主页"）字符串查找页面实例来通知刷新。
/// </summary>
public static class AccountEvents
{
    /// <summary>账号列表发生变化（新增/删除/登录），订阅方应刷新账号列表。</summary>
    public static event Action? AccountsChanged;

    public static void NotifyAccountsChanged()
    {
        AccountsChanged?.Invoke();
    }
}
