using System;
using System.Collections.Generic;
using System.Linq;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>槽位内容发生变化的通知参数</summary>
public class PluginSlotChangedEventArgs : EventArgs
{
    public string SlotId { get; init; } = string.Empty;

    public string PluginId { get; init; } = string.Empty;

    public string ItemId { get; init; } = string.Empty;

    /// <summary>true 表示移除，false 表示新增/更新</summary>
    public bool Removed { get; init; }
}

/// <summary>
/// 插件 UI 槽位注册表。
///
/// 启动器在既有页面上预留若干具名槽位（见 <see cref="KnownSlots"/>），插件把自绘的
/// Avalonia 控件注册进来，由桌面层的 <c>PluginSlotHost</c> 渲染到对应位置。
///
/// 设计约定：
/// <list type="bullet">
/// <item>注册表只存"哪条内容属于哪个槽位"，不引用任何 UI 类型（<see cref="PluginSlotItem.Content"/> 是 <see cref="object"/>）；</item>
/// <item>页面创建晚于插件注册（插件在 MainWindow 构造里加载），所以宿主必须**主动拉取**当前内容，
/// 而不是等推送；<see cref="Changed"/> 只是"该刷新了"的通知；</item>
/// <item>**槽位 id 是开放字符串，不是封闭名单**：任何 id 都能注册。
/// <see cref="KnownSlots"/> 只是"启动器保证有宿主容器"的那几个（写错 id 会记一条警告，
/// 但不会被拒）——因为插件还有 <c>IPluginContext.GetUiRoot()</c> 这条路可以自己找控件挂载，
/// 注册表不该成为唯一的门；</item>
/// <item>插件卸载时由 <see cref="ClearPluginSlotContent"/> 统一清空。</item>
/// </list>
/// </summary>
public static class PluginSlotRegistry
{
    /// <summary>
    /// 当前版本提供宿主容器的槽位。
    /// 插件可用 <see cref="IsKnownSlot"/> 或 <c>IPluginContext.GetSlotIds()</c> 探测。
    /// </summary>
    public static IReadOnlyList<string> KnownSlots { get; } = new[]
    {
        // 崩溃弹窗的按钮行（用户点「分析崩溃日志」的那一行）
        "crash.dialog.actions",

        // 崩溃弹窗里分析结论的下方
        "crash.dialog.analysis.after",

        // 「更多 → 崩溃分析」页分析详情下方
        "crash.page.analysis.after",

        // 「更多 → 崩溃分析」页顶部工具栏
        "crash.page.toolbar",
    };

    private static readonly object Gate = new();
    private static readonly Dictionary<string, List<(int Seq, PluginSlotItem Item)>> Slots = new(StringComparer.Ordinal);
    private static int _seq;

    /// <summary>槽位内容变化通知（可能在非 UI 线程触发，订阅方自行 marshal）</summary>
    public static event EventHandler<PluginSlotChangedEventArgs>? Changed;

    /// <summary>
    /// 注册（或更新）一条槽位内容。
    /// </summary>
    /// <param name="slotId">槽位标识，必须命中 <see cref="KnownSlots"/></param>
    /// <param name="pluginId">插件 ID（由 PluginContext 自动带上）</param>
    /// <param name="itemId">插件内唯一的内容标识；同 id 重复注册视为更新</param>
    /// <param name="content">插件提供的 Avalonia 控件（Core 侧只当 object 存）</param>
    /// <param name="order">排序值，小的在前；相同则按注册顺序</param>
    /// <returns>是否注册成功；内容为 null 或标识为空时返回 false</returns>
    public static bool AddSlotContent(string slotId, string pluginId, string itemId, object? content, int order = 0)
    {
        if (string.IsNullOrWhiteSpace(slotId) || !IsKnownSlot(slotId))
        {
            // 开放设计：仍然接受，只是提醒"启动器这边没有保证的宿主"
            DebugLogger.Warn("PluginSlot",
                $"槽位「{slotId}」不在启动器保证的名单里（插件 {pluginId}）：已登记，但只有当该 id 的宿主出现时才会渲染。"
                + " 想完全自己决定挂在哪，请改用 IPluginContext.GetUiRoot() 自行遍历挂载。");
        }
        if (string.IsNullOrWhiteSpace(slotId) || content is null || string.IsNullOrWhiteSpace(itemId))
        {
            DebugLogger.Warn("PluginSlot", $"插件 {pluginId} 注册槽位「{slotId}」失败：内容或 itemId 为空");
            return false;
        }

        var evt = new PluginSlotChangedEventArgs { SlotId = slotId, PluginId = pluginId, ItemId = itemId };
        lock (Gate)
        {
            if (!Slots.TryGetValue(slotId, out var list))
            {
                list = new List<(int, PluginSlotItem)>();
                Slots[slotId] = list;
            }

            var existing = list.FindIndex(e => string.Equals(e.Item.PluginId, pluginId, StringComparison.Ordinal)
                                            && string.Equals(e.Item.ItemId, itemId, StringComparison.Ordinal));
            var item = new PluginSlotItem
            {
                SlotId = slotId,
                PluginId = pluginId,
                ItemId = itemId,
                Content = content,
                Order = order
            };

            if (existing >= 0)
            {
                list[existing] = (list[existing].Seq, item);
            }
            else
            {
                list.Add((++_seq, item));
            }
        }

        RaiseChanged(evt);
        return true;
    }

    /// <summary>移除一条槽位内容</summary>
    public static bool RemoveSlotContent(string slotId, string pluginId, string itemId)
    {
        lock (Gate)
        {
            if (!Slots.TryGetValue(slotId, out var list)) return false;

            var removed = list.RemoveAll(e => string.Equals(e.Item.PluginId, pluginId, StringComparison.Ordinal)
                                           && string.Equals(e.Item.ItemId, itemId, StringComparison.Ordinal));
            if (removed == 0) return false;
        }

        RaiseChanged(new PluginSlotChangedEventArgs { SlotId = slotId, PluginId = pluginId, ItemId = itemId, Removed = true });
        return true;
    }

    /// <summary>清空某个槽位里属于指定插件的全部内容</summary>
    public static int ClearSlotContent(string slotId, string pluginId)
    {
        int removed;
        lock (Gate)
        {
            if (!Slots.TryGetValue(slotId, out var list)) return 0;
            removed = list.RemoveAll(e => string.Equals(e.Item.PluginId, pluginId, StringComparison.Ordinal));
        }

        if (removed > 0)
        {
            RaiseChanged(new PluginSlotChangedEventArgs { SlotId = slotId, PluginId = pluginId, ItemId = "*", Removed = true });
        }
        return removed;
    }

    /// <summary>清空某个插件在所有槽位里的内容（插件卸载时调用）</summary>
    public static int ClearPluginSlotContent(string pluginId)
    {
        var touched = new List<string>();
        int total = 0;

        lock (Gate)
        {
            foreach (var (slotId, list) in Slots)
            {
                var removed = list.RemoveAll(e => string.Equals(e.Item.PluginId, pluginId, StringComparison.Ordinal));
                if (removed > 0)
                {
                    total += removed;
                    touched.Add(slotId);
                }
            }
        }

        foreach (var slotId in touched)
        {
            RaiseChanged(new PluginSlotChangedEventArgs { SlotId = slotId, PluginId = pluginId, ItemId = "*", Removed = true });
        }
        return total;
    }

    /// <summary>取某个槽位当前应渲染的内容（按 Order 再按注册顺序排序）</summary>
    public static IReadOnlyList<PluginSlotItem> GetSlotItems(string slotId)
    {
        lock (Gate)
        {
            if (!Slots.TryGetValue(slotId, out var list)) return Array.Empty<PluginSlotItem>();
            return list.OrderBy(e => e.Item.Order).ThenBy(e => e.Seq).Select(e => e.Item).ToList();
        }
    }

    /// <summary>当前有内容的槽位 id</summary>
    public static IReadOnlyList<string> GetActiveSlotIds()
    {
        lock (Gate)
        {
            return Slots.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
        }
    }

    public static bool IsKnownSlot(string slotId) =>
        !string.IsNullOrWhiteSpace(slotId) &&
        KnownSlots.Any(s => string.Equals(s, slotId, StringComparison.Ordinal));

    /// <summary>当前注册的内容总数（测试用）</summary>
    public static int TotalItemCount
    {
        get { lock (Gate) { return Slots.Sum(kv => kv.Value.Count); } }
    }

    /// <summary>清空全部状态（测试用；插件卸载请用 ClearPluginSlotContent）</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            Slots.Clear();
            _seq = 0;
            Changed = null;
        }
    }

    private static void RaiseChanged(PluginSlotChangedEventArgs args)
    {
        try
        {
            Changed?.Invoke(null, args);
        }
        catch (Exception ex)
        {
            // 订阅方（UI 宿主）出错不能影响插件的注册调用
            DebugLogger.Error("PluginSlot", $"槽位变更通知处理失败: {ex.Message}");
        }
    }
}
