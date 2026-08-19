using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using ObsMCLauncher.Core.Services;

namespace ObsMCLauncher.Core.Models;

/// <summary>主页组件尺寸档位</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HomeCardSize
{
    /// <summary>紧凑</summary>
    Small = 0,

    /// <summary>标准宽度（默认）</summary>
    Medium = 1,

    /// <summary>加宽，约两倍标准宽</summary>
    Large = 2,

    /// <summary>占满整行</summary>
    Fill = 3
}

/// <summary>布局中的单个组件引用</summary>
public class HomeComponentConfig
{
    /// <summary>组件 ID：内置组件为短 id，插件组件为 {pluginId}.{id}</summary>
    public string Id { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HomeCardSize Size { get; set; } = HomeCardSize.Medium;
}

/// <summary>主页的一行，行内组件横向排列并自动换行</summary>
public class HomeRowConfig
{
    public List<HomeComponentConfig> Components { get; set; } = [];
}

/// <summary>主页整体布局：行的垂直列表</summary>
public class HomeLayoutConfig
{
    public List<HomeRowConfig> Rows { get; set; } = [];

    public bool Contains(string componentId) =>
        Rows.Any(r => r.Components.Any(c => c.Id == componentId));

    /// <summary>移除布局中所有同名组件，返回是否发生了移除</summary>
    public bool Remove(string componentId)
    {
        var removed = false;
        foreach (var row in Rows)
        {
            if (row.Components.RemoveAll(c => c.Id == componentId) > 0)
            {
                removed = true;
            }
        }
        return removed;
    }

    /// <summary>追加组件到最后一行，没有行时先建一行</summary>
    public void Append(string componentId, HomeCardSize size)
    {
        if (Rows.Count == 0)
        {
            Rows.Add(new HomeRowConfig());
        }
        Rows[^1].Components.Add(new HomeComponentConfig { Id = componentId, Size = size });
    }

    /// <summary>清理空行；清空后至少保留一个空行，保证主页可放置组件</summary>
    public void RemoveEmptyRows()
    {
        Rows.RemoveAll(r => r.Components.Count == 0);
        if (Rows.Count == 0)
        {
            Rows.Add(new HomeRowConfig());
        }
    }

    /// <summary>
    /// 从旧版 HomeCards 配置（IsEnabled/Order）推导默认布局。
    /// 未启用的卡片不进布局，之后仍可从组件库重新添加。
    /// </summary>
    public static HomeLayoutConfig CreateDefault(List<HomeCardConfig>? legacyCards)
    {
        var legacy = legacyCards ?? [];

        HomeCardSize SizeOf(string id)
        {
            var descriptor = HomeComponentRegistry.TryGet(id);
            return descriptor?.DefaultSize ?? HomeCardSize.Medium;
        }

        var layout = new HomeLayoutConfig();

        // 欢迎卡：启用时独占一行
        var welcomeEnabled = legacy.FirstOrDefault(c => c.CardId == HomeComponentRegistry.WelcomeId)?.IsEnabled ?? true;
        if (welcomeEnabled)
        {
            layout.Rows.Add(new HomeRowConfig
            {
                Components = [new HomeComponentConfig { Id = HomeComponentRegistry.WelcomeId, Size = HomeCardSize.Fill }]
            });
        }

        // 卡片行：内置卡片 + 插件卡片按旧 Order 排序，禁用的跳过
        int LegacyOrder(string id, int fallback) =>
            legacy.FirstOrDefault(c => c.CardId == id && !c.IsPluginCard)?.Order ?? fallback;

        var ordered = new List<(string Id, int Order)>
        {
            (HomeComponentRegistry.NewsId, LegacyOrder(HomeComponentRegistry.NewsId, 1)),
            (HomeComponentRegistry.MultiplayerId, LegacyOrder(HomeComponentRegistry.MultiplayerId, 2)),
            (HomeComponentRegistry.ModsId, LegacyOrder(HomeComponentRegistry.ModsId, 3))
        };
        ordered.AddRange(legacy.Where(c => c.IsPluginCard).Select(c => (c.CardId, c.Order)));

        var cardRow = new HomeRowConfig();
        foreach (var (id, _) in ordered.OrderBy(x => x.Order))
        {
            var cfg = legacy.FirstOrDefault(c => c.CardId == id);
            if (cfg?.IsEnabled == false) continue;
            cardRow.Components.Add(new HomeComponentConfig { Id = id, Size = SizeOf(id) });
        }
        if (cardRow.Components.Count > 0)
        {
            layout.Rows.Add(cardRow);
        }

        // 操作行：分隔线 + 账号 + 版本 + 启动 + 日志开关
        layout.Rows.Add(new HomeRowConfig
        {
            Components =
            [
                new HomeComponentConfig { Id = HomeComponentRegistry.SeparatorId, Size = HomeCardSize.Fill },
                new HomeComponentConfig { Id = HomeComponentRegistry.AccountPickerId, Size = HomeCardSize.Medium },
                new HomeComponentConfig { Id = HomeComponentRegistry.VersionPickerId, Size = HomeCardSize.Medium },
                new HomeComponentConfig { Id = HomeComponentRegistry.LaunchButtonId, Size = HomeCardSize.Medium },
                new HomeComponentConfig { Id = HomeComponentRegistry.LogToggleId, Size = HomeCardSize.Medium }
            ]
        });

        return layout;
    }
}
