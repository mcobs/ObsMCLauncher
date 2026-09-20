using System;

namespace ObsMCLauncher.Core.Models;

/// <summary>
/// 崩溃报告文件类型
/// </summary>
public enum CrashReportKind
{
    /// <summary>Minecraft 崩溃报告（crash-reports/crash-*.txt）</summary>
    MinecraftCrashReport,

    /// <summary>JVM 致命错误日志（hs_err_pid*.log，通常是显卡驱动或 native 崩溃）</summary>
    JvmFatalErrorLog
}

/// <summary>
/// 一个崩溃报告文件的元信息
/// </summary>
public class CrashReportInfo
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    public DateTime CreatedTime { get; set; }

    /// <summary>所属版本名；位于游戏主目录时为「主目录」</summary>
    public string? VersionName { get; set; }

    public CrashReportKind Kind { get; set; }

    public string FormattedSize
    {
        get
        {
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            return $"{Size / 1024.0 / 1024.0:F1} MB";
        }
    }

    public string FormattedCreatedTime => CreatedTime.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>列表中展示的类别标签</summary>
    public string KindLabel => Kind == CrashReportKind.JvmFatalErrorLog ? "JVM 崩溃" : "崩溃报告";
}

/// <summary>
/// 崩溃原因置信度
/// </summary>
public enum CrashConfidence
{
    /// <summary>有直接证据（如明确的异常签名）</summary>
    High,

    /// <summary>有较强的间接证据</summary>
    Medium,

    /// <summary>仅兜底推测</summary>
    Low
}

/// <summary>
/// 一条识别出的崩溃原因
/// </summary>
public class CrashCause
{
    /// <summary>分类键（Memory / Java / Graphics / ModLoading / Mixin / ModCompat / Config / World / Network / Unknown），UI 据此选图标</summary>
    public string Category { get; set; } = "";

    /// <summary>一句话结论</summary>
    public string Title { get; set; } = "";

    /// <summary>证据：引用的报告原文或推导过程</summary>
    public string Evidence { get; set; } = "";

    /// <summary>修复建议</summary>
    public string Suggestion { get; set; } = "";

    public CrashConfidence Confidence { get; set; }

    /// <summary>分类的中文展示名</summary>
    public string CategoryLabel => Category switch
    {
        "Memory" => "内存",
        "Java" => "Java",
        "Graphics" => "显卡/显示",
        "ModLoading" => "Mod 加载",
        "Mixin" => "Mixin 冲突",
        "ModCompat" => "Mod 兼容性",
        "Config" => "配置文件",
        "World" => "世界/存档",
        "Network" => "网络",
        "FileAccess" => "文件访问",
        _ => "其他"
    };

    public string ConfidenceLabel => Confidence switch
    {
        CrashConfidence.High => "高置信度",
        CrashConfidence.Medium => "中置信度",
        _ => "低置信度"
    };
}

/// <summary>
/// 崩溃报告分析结果
/// </summary>
public class CrashAnalysisResult
{
    public string FileName { get; set; } = "";
    public CrashReportKind Kind { get; set; }

    /// <summary>报告里的崩溃时间（原文）</summary>
    public string? CrashTime { get; set; }

    /// <summary>Description 行内容</summary>
    public string? Description { get; set; }

    /// <summary>首行异常（含消息）</summary>
    public string? ExceptionSummary { get; set; }

    public string? MinecraftVersion { get; set; }
    public string? LoaderInfo { get; set; }
    public string? JavaVersion { get; set; }
    public string? OperatingSystem { get; set; }

    /// <summary>识别出的可能原因，按置信度与规则优先级排序</summary>
    public List<CrashCause> Causes { get; set; } = new();

    /// <summary>从堆栈 / Suspected Mod / Mod 列表对照提取的可疑 Mod 标识</summary>
    public List<string> SuspectedMods { get; set; } = new();

    /// <summary>报告原文前若干行（用于预览）</summary>
    public string RawPreview { get; set; } = "";

    /// <summary>最可能原因的一句话标题</summary>
    public string Headline => Causes.Count > 0 ? Causes[0].Title : "未能确定具体原因";
}
