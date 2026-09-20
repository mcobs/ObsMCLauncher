using System;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Crash;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 内部崩溃模型 → 面向插件的 DTO 映射。
/// 集中在一处的好处：插件可见的字段与内部模型解耦（内部怎么改都不用动插件），
/// 且这段映射不需要 UI 就能单测。
/// </summary>
public static class PluginCrashMapper
{
    public static string KindToString(CrashReportKind kind) =>
        kind == CrashReportKind.JvmFatalErrorLog ? "JvmFatalError" : "Minecraft";

    public static string ConfidenceToString(CrashConfidence confidence) => confidence switch
    {
        CrashConfidence.High => "High",
        CrashConfidence.Medium => "Medium",
        _ => "Low"
    };

    /// <summary>报告文件信息 → 插件 DTO</summary>
    public static PluginCrashReportInfo ToPlugin(CrashReportInfo info, int exitCode = 0, bool reportFound = true) =>
        new()
        {
            ReportPath = info.FullPath,
            FileName = info.FileName,
            VersionId = info.VersionName,
            Kind = KindToString(info.Kind),
            CreatedTime = info.CreatedTime,
            SizeBytes = info.Size,
            ReportFound = reportFound,
            ExitCode = exitCode
        };

    /// <summary>
    /// 崩溃判定结果 → 插件 DTO。
    /// 用于 <c>CrashDetected</c> 事件负载：此时可能尚未找到报告文件（<see cref="PluginCrashReportInfo.ReportFound"/> 为 false）。
    /// </summary>
    public static PluginCrashReportInfo ToPlugin(GameCrashInfo info)
    {
        long size = 0;
        if (info.ReportFound && !string.IsNullOrEmpty(info.ReportPath))
        {
            try
            {
                var file = new System.IO.FileInfo(info.ReportPath);
                if (file.Exists) size = file.Length;
            }
            catch
            {
                // 读取大小失败不影响事件本身
            }
        }

        return new PluginCrashReportInfo
        {
            ReportPath = info.ReportPath ?? string.Empty,
            FileName = info.ReportFileName ?? string.Empty,
            VersionId = info.VersionId,
            Kind = KindToString(info.Kind),
            CreatedTime = info.ReportTime ?? DateTime.Now,
            SizeBytes = size,
            ReportFound = info.ReportFound,
            ExitCode = info.ExitCode
        };
    }

    /// <summary>
    /// 内置分析结果 → 插件 DTO。
    /// <paramref name="sanitize"/> 为 true（默认）时对原文预览做脱敏，
    /// 避免插件直接把用户名/token 连同报告发到外部服务。
    /// </summary>
    public static PluginCrashAnalysis ToPlugin(CrashAnalysisResult result, bool sanitize = true)
    {
        return new PluginCrashAnalysis
        {
            ReportPath = result.FileName, // 调用方若知道完整路径可自行覆盖
            FileName = result.FileName,
            Headline = result.Headline,
            MinecraftVersion = result.MinecraftVersion,
            LoaderInfo = result.LoaderInfo,
            JavaVersion = result.JavaVersion,
            OperatingSystem = result.OperatingSystem,
            CrashTime = result.CrashTime,
            Description = result.Description,
            ExceptionSummary = result.ExceptionSummary,
            Causes = result.Causes.Select(ToPlugin).ToList(),
            SuspectedMods = result.SuspectedMods.ToArray(),
            RawPreview = sanitize ? CrashReportSanitizer.Sanitize(result.RawPreview) : result.RawPreview
        };
    }

    public static PluginCrashCause ToPlugin(CrashCause cause) =>
        new()
        {
            Category = cause.Category,
            Title = cause.Title,
            Evidence = sanitizeEvidence(cause.Evidence),
            Suggestion = cause.Suggestion,
            Confidence = ConfidenceToString(cause.Confidence)
        };

    /// <summary>证据字段常引用报告原文（可能含用户名），一并脱敏</summary>
    private static string sanitizeEvidence(string evidence) => CrashReportSanitizer.Sanitize(evidence);
}
