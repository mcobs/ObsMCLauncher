using System;
using ObsMCLauncher.Core.Plugins;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 「用户当前正在看的崩溃上下文」——崩溃弹窗 / 崩溃分析页当前选中的那份报告。
///
/// 用途：
/// <list type="bullet">
/// <item><c>IPluginContext.GetActiveCrashContext()</c> 读它；</item>
/// <item>槽位宿主把它作为插件内容的 DataContext 兜底，让插件的按钮知道"该分析哪份日志"。</item>
/// </list>
///
/// 采用"最后写入者胜"：弹窗与页面同时存在时以最近一次设置为准（弹窗是模态的，实际不会并发操作）。
/// </summary>
public static class PluginCrashContextService
{
    private static PluginSlotContext? _current;

    /// <summary>当前上下文；没有时为 null</summary>
    public static PluginSlotContext? Current => _current;

    /// <summary>设置当前崩溃上下文（报告路径 + 版本）</summary>
    public static void SetCrashContext(string? reportPath, string? versionId)
    {
        if (string.IsNullOrEmpty(reportPath) && string.IsNullOrEmpty(versionId))
        {
            _current = null;
            return;
        }

        // SlotId 由各槽位宿主在给插件内容设 DataContext 时补齐，这里只承载"看的是哪份报告"
        _current = new PluginSlotContext
        {
            SlotId = string.Empty,
            CrashReportPath = reportPath,
            VersionId = versionId
        };
    }

    /// <summary>
    /// 清空上下文，但仅当它仍指向指定报告时（避免弹窗关闭时把页面的上下文也清掉）
    /// </summary>
    public static void ClearIf(string? reportPath)
    {
        if (_current is null) return;
        if (string.Equals(_current.CrashReportPath, reportPath, StringComparison.Ordinal))
        {
            _current = null;
        }
    }

    /// <summary>无条件清空（测试用）</summary>
    public static void ClearForTests() => _current = null;
}
