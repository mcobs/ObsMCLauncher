using System;
using System.Collections.Generic;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 暴露给插件的崩溃报告精简信息。
/// 与 <see cref="PluginVersionInfo"/> / <see cref="PluginAccountInfo"/> 同约定：
/// 内部模型可以自由改，面向插件的字段单独维护，避免插件随内部重构而编译失败。
/// </summary>
public class PluginCrashReportInfo
{
    /// <summary>报告文件的完整路径</summary>
    public string ReportPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>所属版本 ID；报告位于游戏主目录时为「主目录」或空</summary>
    public string? VersionId { get; set; }

    /// <summary>报告类型："Minecraft"（crash-reports/crash-*.txt）或 "JvmFatalError"（hs_err_pid*.log）</summary>
    public string Kind { get; set; } = "Minecraft";

    /// <summary>报告写入时间（本地时间）</summary>
    public DateTime CreatedTime { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>本次是否定位到了报告文件。崩溃事件里可能为 false（报告还没落盘）</summary>
    public bool ReportFound { get; set; } = true;

    /// <summary>触发这次崩溃的进程退出码；非崩溃场景（如手动分析）为 0</summary>
    public int ExitCode { get; set; }
}

/// <summary>插件可读的崩溃原因（内置规则分析结果的一条）</summary>
public class PluginCrashCause
{
    /// <summary>分类：Memory / Java / Graphics / ModLoading / Mixin / ModCompat / Config / World / FileAccess / Unknown</summary>
    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Evidence { get; set; } = string.Empty;

    public string Suggestion { get; set; } = string.Empty;

    /// <summary>置信度：High / Medium / Low</summary>
    public string Confidence { get; set; } = "Low";
}

/// <summary>
/// 内置规则引擎对某份崩溃报告的分析结果（只读快照）。
/// 插件可以在此基础上做自己的判断（例如喂给模型当上下文），也可以完全不用。
/// </summary>
public class PluginCrashAnalysis
{
    public string ReportPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>最可能原因的一句话结论</summary>
    public string Headline { get; set; } = string.Empty;

    public string? MinecraftVersion { get; set; }

    public string? LoaderInfo { get; set; }

    public string? JavaVersion { get; set; }

    public string? OperatingSystem { get; set; }

    public string? CrashTime { get; set; }

    /// <summary>报告里的 Description 行</summary>
    public string? Description { get; set; }

    /// <summary>首行异常</summary>
    public string? ExceptionSummary { get; set; }

    public IReadOnlyList<PluginCrashCause> Causes { get; set; } = Array.Empty<PluginCrashCause>();

    /// <summary>从堆栈 / Suspected Mod / Mod 列表提取的可疑 Mod</summary>
    public IReadOnlyList<string> SuspectedMods { get; set; } = Array.Empty<string>();

    /// <summary>报告原文前若干行（已脱敏）</summary>
    public string RawPreview { get; set; } = string.Empty;
}

/// <summary>
/// 槽位内容的上下文：作为宿主容器里插件控件的 DataContext 提供，
/// 让插件知道"我现在挂在哪个槽位、用户正在看哪份报告"。
/// </summary>
public class PluginSlotContext
{
    /// <summary>槽位标识，见 <see cref="PluginSlotRegistry.KnownSlots"/></summary>
    public string SlotId { get; set; } = string.Empty;

    /// <summary>当前上下文对应的崩溃报告路径；无上下文时为 null</summary>
    public string? CrashReportPath { get; set; }

    /// <summary>崩溃报告所属版本 ID</summary>
    public string? VersionId { get; set; }
}

/// <summary>槽位里的一条插件内容</summary>
public class PluginSlotItem
{
    public string SlotId { get; set; } = string.Empty;

    public string PluginId { get; set; } = string.Empty;

    /// <summary>插件内唯一的内容标识（用于注销）</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>插件提供的 Avalonia 控件（Core 以 object 承载，不引用 UI 框架）</summary>
    public object Content { get; set; } = null!;

    /// <summary>排序值，小的在前；相同则按注册顺序</summary>
    public int Order { get; set; }
}
