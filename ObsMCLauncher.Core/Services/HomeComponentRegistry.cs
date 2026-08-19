using System;
using System.Collections.Generic;
using System.Linq;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services;

/// <summary>主页组件描述符：描述一个可放置到主页的组件（内置组件或插件卡片）</summary>
public sealed class HomeComponentDescriptor
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>图标：SVG path 数据或单字符文本，null 时组件库显示默认图形</summary>
    public string? Icon { get; init; }

    /// <summary>来源插件 ID，null 表示内置组件</summary>
    public string? PluginId { get; init; }

    public HomeCardSize DefaultSize { get; init; } = HomeCardSize.Medium;

    public bool IsPlugin => PluginId != null;
}

/// <summary>组件库分组：内置组件一组，每个插件各一组</summary>
public sealed class HomeComponentGroup
{
    /// <summary>插件 ID，null 表示内置分组</summary>
    public string? PluginId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public List<HomeComponentDescriptor> Components { get; } = [];
}

/// <summary>
/// 主页组件注册表。内置组件首次访问时自动注册；
/// 插件卡片与自定义组件随插件注册事件进入，随插件禁用/移除清理。
/// 线程安全。
/// </summary>
public static class HomeComponentRegistry
{
    private static readonly Dictionary<string, HomeComponentDescriptor> _components = new();
    private static readonly object _lock = new();
    private static bool _builtinsRegistered;

    // 内置组件 ID
    public const string WelcomeId = "welcome";
    public const string NewsId = "news";
    public const string MultiplayerId = "multiplayer";
    public const string ModsId = "mods";

    // 操作区已去组件化：这些 ID 不再注册，仅供旧布局迁移清理时识别
    public const string AccountPickerId = "accountPicker";
    public const string VersionPickerId = "versionPicker";
    public const string LaunchButtonId = "launchButton";
    public const string LogToggleId = "logToggle";
    public const string SeparatorId = "separator";

    // 图标沿用主页卡片的矢量 path，避免依赖系统 emoji 字形
    private const string IconRocket = "M12 2C12 2 6 6 6 12C6 15.31 7.79 18.17 10.5 19.71L12 23L13.5 19.71C16.21 18.17 18 15.31 18 12C18 6 12 2 12 2M12 10C10.9 10 10 9.1 10 8C10 6.9 10.9 6 12 6C13.1 6 14 6.9 14 8C14 9.1 13.1 10 12 10M12 20C12 20 8 17.86 8 12C8 10.5 8.5 9.24 9.3 8.17C9.86 8.69 10.42 9.12 11.16 9.44C12.62 10.08 14.55 10.37 15.5 10.05C15.76 11.5 15.37 12.6 15 13.43C14.5 14.53 12 20 12 20Z";
    private const string IconNews = "M20 2H4C2.9 2 2 2.9 2 4V22L6 18H20C21.1 18 22 17.1 22 16V4C22 2.9 21.1 2 20 2M20 16H5.17L4 17.17V4H20V16M7 9H17V7H7V9M7 13H14V11H7V13Z";
    private const string IconGlobe = "M12 2C6.48 2 2 6.48 2 12S6.48 22 12 22 22 17.52 22 12 17.52 2 12 2M12 20C11.1 20 10.21 19.88 9.36 19.67L10 18L12 16L13.34 13.09L14.35 12H17.5C18.2 12 18.85 12.26 19.35 12.67C18.37 16.8 15.48 20 12 20M7 9L5.77 11.13L5.25 11.77C5.08 11.23 5 10.65 5 10C5 8.94 5.26 7.94 5.71 7.06L7 9M19 10.25C18.03 9.21 16.57 8.5 15 8.5H12.86L10 9.63V12.38L12.41 14.79L13.07 13.25L15 12.5L17 14.5V17.13C14.24 18.37 11 18.37 8.24 17.13L6.83 16.71L6.4 16.29L5.03 16.72C5.16 17.5 5.41 18.25 5.75 18.94C7.21 20.91 9.43 22.02 12 22C16.42 22 20 18.42 20 14C20 12.72 19.65 11.52 19.03 10.5L19 10.25Z";
    private const string IconDownload = "M19 9H15V3H9V9H5L12 16L19 9M5 18V20H19V18H5Z";

    static HomeComponentRegistry() => RegisterBuiltins();

    private static void RegisterBuiltins()
    {
        if (_builtinsRegistered) return;
        _builtinsRegistered = true;

        Register(new HomeComponentDescriptor
        {
            Id = WelcomeId,
            Title = "欢迎卡片",
            Description = "启动器 Logo 与问候语横幅",
            Icon = IconRocket,
            DefaultSize = HomeCardSize.Fill
        });
        Register(new HomeComponentDescriptor
        {
            Id = NewsId,
            Title = "新闻资讯",
            Description = "查看最新的 Minecraft 新闻动态",
            Icon = IconNews,
            DefaultSize = HomeCardSize.Medium
        });
        Register(new HomeComponentDescriptor
        {
            Id = MultiplayerId,
            Title = "多人联机",
            Description = "加入服务器与好友一起游戏",
            Icon = IconGlobe,
            DefaultSize = HomeCardSize.Medium
        });
        Register(new HomeComponentDescriptor
        {
            Id = ModsId,
            Title = "资源下载",
            Description = "下载 Mod、材质包等资源",
            Icon = IconDownload,
            DefaultSize = HomeCardSize.Medium
        });
    }

    /// <summary>注册或更新一个组件（同 ID 再次注册视为更新）</summary>
    public static void Register(HomeComponentDescriptor descriptor)
    {
        if (descriptor == null || string.IsNullOrEmpty(descriptor.Id)) return;
        lock (_lock)
        {
            _components[descriptor.Id] = descriptor;
        }
    }

    /// <summary>注册插件数据卡片，ID 自动加 {pluginId}. 前缀</summary>
    public static void RegisterPluginCard(string pluginId, string cardId, string title, string description,
        string? icon, HomeCardSize defaultSize)
    {
        if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(cardId)) return;
        Register(new HomeComponentDescriptor
        {
            Id = $"{pluginId}.{cardId}",
            Title = title,
            Description = description,
            Icon = icon,
            PluginId = pluginId,
            DefaultSize = defaultSize
        });
    }

    /// <summary>注销组件，返回是否存在</summary>
    public static bool Unregister(string componentId)
    {
        if (string.IsNullOrEmpty(componentId)) return false;
        lock (_lock)
        {
            return _components.Remove(componentId);
        }
    }

    public static HomeComponentDescriptor? TryGet(string componentId)
    {
        if (string.IsNullOrEmpty(componentId)) return null;
        lock (_lock)
        {
            return _components.TryGetValue(componentId, out var descriptor) ? descriptor : null;
        }
    }

    public static bool Contains(string componentId)
    {
        if (string.IsNullOrEmpty(componentId)) return false;
        lock (_lock)
        {
            return _components.ContainsKey(componentId);
        }
    }

    public static IReadOnlyList<HomeComponentDescriptor> GetAll()
    {
        lock (_lock)
        {
            return _components.Values.ToList();
        }
    }

    /// <summary>按来源分组：内置组在前，插件组按插件 ID 排序，供组件库展示</summary>
    public static IReadOnlyList<HomeComponentGroup> GetGrouped()
    {
        lock (_lock)
        {
            var builtin = new HomeComponentGroup { PluginId = null, DisplayName = "内置组件" };
            builtin.Components.AddRange(_components.Values
                .Where(c => c.PluginId == null)
                .OrderBy(c => c.Id));

            var groups = new List<HomeComponentGroup> { builtin };
            foreach (var pluginGroup in _components.Values
                         .Where(c => c.PluginId != null)
                         .GroupBy(c => c.PluginId)
                         .OrderBy(g => g.Key))
            {
                var group = new HomeComponentGroup { PluginId = pluginGroup.Key, DisplayName = $"插件 {pluginGroup.Key}" };
                group.Components.AddRange(pluginGroup.OrderBy(c => c.Id));
                groups.Add(group);
            }
            return groups;
        }
    }

    /// <summary>移除插件注册的所有组件（插件禁用/移除时调用），返回移除数量</summary>
    public static int RemovePluginComponents(string pluginId)
    {
        if (string.IsNullOrEmpty(pluginId)) return 0;
        lock (_lock)
        {
            var keys = _components.Where(kv => kv.Value.PluginId == pluginId)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in keys)
            {
                _components.Remove(key);
            }
            return keys.Count;
        }
    }

    /// <summary>清空插件组件并恢复到仅内置组件的状态（仅用于单元测试隔离）</summary>
    public static void ResetToBuiltinsForTests()
    {
        lock (_lock)
        {
            _components.Clear();
            _builtinsRegistered = false;
            RegisterBuiltins();
        }
    }
}
