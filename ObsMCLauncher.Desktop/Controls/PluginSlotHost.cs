using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Controls;

/// <summary>
/// 插件 UI 槽位宿主：页面上预留的一段容器，用来渲染插件注册进该槽位的内容。
///
/// 用法（XAML 里把启动器自己的控件直接写成子元素）：
/// <code>
/// &lt;controls:PluginSlotHost SlotId="crash.dialog.actions" Orientation="Horizontal" Spacing="8"&gt;
///   &lt;Button Content="查看原文" /&gt;   &lt;!-- 启动器自己的控件 --&gt;
/// &lt;/controls:PluginSlotHost&gt;
/// </code>
///
/// 关于"插件能改到多少"（已确认采用最大自由度方案）：
/// <list type="bullet">
/// <item>本控件**就是**交给插件的那只容器（<c>IPluginContext.GetSlotHost</c> 返回它），
/// 里面既有启动器控件也有插件内容，所以插件可以任意增删改——包括隐藏/改写启动器的按钮；</item>
/// <item>刷新只回收"注册表托管"的控件（<see cref="_pluginControls"/>），
/// XAML 里声明的启动器控件、以及插件自己直接 Add 进来的控件都不会被清掉，
/// 否则每次刷新都会把页面清空；</item>
/// <item>页面/弹窗是随用随建的，所以宿主在挂上视觉树时**主动拉取**一次注册表内容
/// （插件在 MainWindow 构造期就已注册，早于页面创建）。</item>
/// </list>
/// </summary>
public class PluginSlotHost : StackPanel
{
    /// <summary>槽位标识，必须是 <see cref="PluginSlotRegistry.KnownSlots"/> 里的值</summary>
    public static readonly StyledProperty<string?> SlotIdProperty =
        AvaloniaProperty.Register<PluginSlotHost, string?>(nameof(SlotId));

    /// <summary>当前上下文（崩溃报告路径等），会作为插件内容的 DataContext 兜底</summary>
    public static readonly StyledProperty<PluginSlotContext?> SlotContextProperty =
        AvaloniaProperty.Register<PluginSlotHost, PluginSlotContext?>(nameof(SlotContext));

    /// <summary>宿主实例表：slotId → 当前挂载的宿主（同一槽位同时只应挂一个）</summary>
    private static readonly Dictionary<string, PluginSlotHost> MountedHosts = new(StringComparer.Ordinal);
    private static readonly object MountedHostsLock = new();

    private readonly List<Control> _pluginControls = new();

    public string? SlotId
    {
        get => GetValue(SlotIdProperty);
        set => SetValue(SlotIdProperty, value);
    }

    public PluginSlotContext? SlotContext
    {
        get => GetValue(SlotContextProperty);
        set => SetValue(SlotContextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SlotContextProperty || change.Property == SlotIdProperty)
        {
            ApplyContextToPluginControls();
        }
    }

    /// <summary>
    /// 上下文变化时同步给已挂载的插件控件。
    /// 只改"当前 DataContext 是我们给的 PluginSlotContext"的控件——
    /// 插件自己设过 DataContext 的不能覆盖。
    /// </summary>
    private void ApplyContextToPluginControls()
    {
        if (string.IsNullOrWhiteSpace(SlotId)) return;

        foreach (var control in _pluginControls)
        {
            if (control.DataContext is PluginSlotContext && SlotContext is not null)
            {
                control.DataContext = new PluginSlotContext
                {
                    SlotId = SlotId!,
                    CrashReportPath = SlotContext.CrashReportPath,
                    VersionId = SlotContext.VersionId
                };
            }
        }
    }

    public PluginSlotHost()
    {
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    /// <summary>取某个槽位当前挂载的宿主容器（供 IPluginContext.GetSlotHost）</summary>
    public static object? TryGetMountedHost(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId)) return null;
        lock (MountedHostsLock)
        {
            return MountedHosts.TryGetValue(slotId, out var host) ? host : null;
        }
    }

    private bool _subscribed;

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var slotId = SlotId;
        if (string.IsNullOrWhiteSpace(slotId)) return;

        lock (MountedHostsLock)
        {
            MountedHosts[slotId!] = this;
        }

        // 订阅放在挂载时（与卸载成对），否则 detach 之后再 attach 就收不到通知了
        if (!_subscribed)
        {
            PluginSlotRegistry.Changed += OnRegistryChanged;
            _subscribed = true;
        }

        Refresh();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var slotId = SlotId;
        if (!string.IsNullOrWhiteSpace(slotId))
        {
            lock (MountedHostsLock)
            {
                if (MountedHosts.TryGetValue(slotId!, out var host) && ReferenceEquals(host, this))
                {
                    MountedHosts.Remove(slotId!);
                }
            }
        }

        if (_subscribed)
        {
            PluginSlotRegistry.Changed -= OnRegistryChanged;
            _subscribed = false;
        }
    }

    private void OnRegistryChanged(object? sender, PluginSlotChangedEventArgs e)
    {
        // 注册表可能在插件线程被改，刷新必须回到 UI 线程
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        }
    }

    /// <summary>按注册表内容重建插件部分（保留启动器控件与插件自行添加的控件）</summary>
    public void Refresh()
    {
        if (string.IsNullOrWhiteSpace(SlotId)) return;

        // 先摘掉上一轮由注册表托管的控件
        foreach (var control in _pluginControls)
        {
            Children.Remove(control);
        }
        _pluginControls.Clear();

        var items = PluginSlotRegistry.GetSlotItems(SlotId!);
        foreach (var item in items)
        {
            if (item.Content is not Control control)
            {
                DebugLogger.Warn("PluginSlotHost", $"槽位「{SlotId}」的内容不是 Avalonia 控件，已跳过（插件 {item.PluginId}）");
                continue;
            }

            // 只在插件没自己设 DataContext 时兜底，避免覆盖插件的数据绑定。
            // 用副本而不是共享实例：SlotId 是每个槽位自己的，共享会互相覆盖。
            if (control.DataContext is null && SlotContext is not null)
            {
                control.DataContext = new PluginSlotContext
                {
                    SlotId = SlotId!,
                    CrashReportPath = SlotContext.CrashReportPath,
                    VersionId = SlotContext.VersionId
                };
            }

            _pluginControls.Add(control);
            Children.Add(control);
        }

        if (items.Count > 0)
        {
            DebugLogger.Info("PluginSlotHost", $"槽位「{SlotId}」渲染了 {_pluginControls.Count} 项插件内容");
        }
    }
}
