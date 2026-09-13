using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>当前供电方式</summary>
public enum PowerSource
{
    /// <summary>未知（平台不支持检测，或读取失败）</summary>
    Unknown,

    /// <summary>市电供电</summary>
    AlternatingCurrent,

    /// <summary>电池供电</summary>
    Battery
}

/// <summary>
/// 跨平台电池状态监视：动图在电池供电时暂停（设计文档 §6.5）。
/// </summary>
/// <remarks>
/// <para>
/// 用轮询而不是平台事件（Windows 的 <c>WM_POWERBROADCAST</c> 需要窗口句柄与消息循环，
/// Linux 没有统一事件源）。电池状态变化是分钟级的，30 秒轮询的开销可以忽略。
/// </para>
/// <para>
/// **macOS 未实现**：<c>IOPSCopyPowerSourcesInfo</c> 需要 IOKit 的 CFType 互操作，
/// 引入的复杂度与收益不成比例。该平台返回 <see cref="PowerSource.Unknown"/>，
/// 结果是"电池暂停"功能静默失效——只影响省电，不影响正确性。
/// </para>
/// </remarks>
public sealed class PowerStatusMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private Timer? _timer;
    private bool _disposed;

    /// <summary>当前供电方式</summary>
    public PowerSource Current { get; private set; } = PowerSource.Unknown;

    /// <summary>是否处于电池供电</summary>
    public bool IsOnBattery => Current == PowerSource.Battery;

    /// <summary>供电方式发生变化</summary>
    public event EventHandler? Changed;

    /// <summary>立即读一次并启动轮询</summary>
    public void Start()
    {
        if (_disposed) return;

        Update();
        _timer ??= new Timer(_ => Update(), null, PollInterval, PollInterval);
    }

    /// <summary>停止轮询（不动 <see cref="Current"/>，便于恢复后立即取用）</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Update()
    {
        if (_disposed) return;

        var value = Read();
        if (value == Current) return;

        Current = value;
        DebugLogger.Info("Wallpaper", $"供电方式变化：{value}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>读取当前供电方式（不抛异常）</summary>
    internal static PowerSource Read()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsLinux()) return ReadLinux();

            return PowerSource.Unknown;
        }
        catch
        {
            return PowerSource.Unknown;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Windows：GetSystemPowerStatus
    // ────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;      // 0 = 离线（电池），1 = 在线（市电），255 = 未知
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private static PowerSource ReadWindows()
    {
        if (!GetSystemPowerStatus(out var status)) return PowerSource.Unknown;

        return status.AcLineStatus switch
        {
            0 => PowerSource.Battery,
            1 => PowerSource.AlternatingCurrent,
            _ => PowerSource.Unknown
        };
    }

    // ────────────────────────────────────────────────────────────────
    // Linux：/sys/class/power_supply/*/status
    // ────────────────────────────────────────────────────────────────

    private static PowerSource ReadLinux()
    {
        const string root = "/sys/class/power_supply";
        if (!Directory.Exists(root)) return PowerSource.Unknown;

        var sawAc = false;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var statusFile = Path.Combine(dir, "status");
            if (!File.Exists(statusFile)) continue;

            string status;
            try { status = File.ReadAllText(statusFile).Trim(); }
            catch { continue; }

            if (status.Equals("Discharging", StringComparison.OrdinalIgnoreCase)) return PowerSource.Battery;
            if (status.Equals("Charging", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Full", StringComparison.OrdinalIgnoreCase))
            {
                sawAc = true;
            }
        }

        return sawAc ? PowerSource.AlternatingCurrent : PowerSource.Unknown;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
