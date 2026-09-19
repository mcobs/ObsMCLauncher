using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 窗口边框策略的全局开关。
///
/// 默认所有窗口走自定义标题栏：把客户区扩展到系统标题栏区域并去掉系统 chrome，
/// 由 <c>WindowTitleBar</c> 自绘标题栏；只有配置里打开「使用系统标题栏」才退回原生标题栏。
/// 切换只改两个窗口属性，不重建窗口，因此能实时生效。
/// </summary>
public static class WindowChrome
{
    /// <summary>当前是否使用操作系统原生标题栏</summary>
    public static bool UseSystemTitleBar { get; private set; }

    /// <summary>策略变化通知，自定义标题栏据此显隐</summary>
    public static event EventHandler? Changed;

    /// <summary>启动时用配置初始化，必须在创建任何窗口之前调用</summary>
    public static void Initialize(bool useSystemTitleBar) => UseSystemTitleBar = useSystemTitleBar;

    public static void SetUseSystemTitleBar(bool useSystemTitleBar)
    {
        if (UseSystemTitleBar == useSystemTitleBar) return;

        UseSystemTitleBar = useSystemTitleBar;

        foreach (var window in GetOpenWindows())
        {
            Apply(window);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>把当前策略套到单个窗口上</summary>
    public static void Apply(Window window)
    {
        if (UseSystemTitleBar)
        {
            // 两次赋值都发生在「仍在扩展客户区」的状态下，避免出现无标题栏的中间态
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.Default;
            window.ExtendClientAreaToDecorationsHint = false;
        }
        else
        {
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        }
    }

    private static IReadOnlyList<Window> GetOpenWindows()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows
            : Array.Empty<Window>();
}