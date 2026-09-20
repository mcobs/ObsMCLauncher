using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services.Crash;

/// <summary>
/// Minecraft 崩溃报告 / JVM 致命错误日志（hs_err）分析器。
/// 纯文本解析 + 有序规则库：每条规则命中后产出带证据与建议的 <see cref="CrashCause"/>，
/// 另做通用提取（版本 / Java / 加载器 / 可疑 Mod）。
/// 不依赖任何游戏目录状态，可独立单元测试。
/// </summary>
public class CrashReportAnalyzer
{
    private static CrashReportAnalyzer? _instance;
    public static CrashReportAnalyzer Instance => _instance ??= new CrashReportAnalyzer();

    private CrashReportAnalyzer() { }

    /// <summary>读取上限：崩溃报告正常 &lt; 500KB，超过 2MB 视为异常文件只取头部</summary>
    private const int MaxReadBytes = 2 * 1024 * 1024;

    private const int PreviewLineCount = 60;

    // 官方/运行时包前缀：堆栈可疑 Mod 提取时排除
    private static readonly string[] OfficialPackagePrefixes =
    {
        "java.", "javax.", "jdk.", "sun.", "com.sun.",
        "net.minecraft", "com.mojang",
        "net.minecraftforge", "cpw.mods", "net.neoforged",
        "net.fabricmc", "org.quiltmc",
        "org.spongepowered", "org.objectweb.asm", "org.apache", "com.google",
        "io.netty", "org.lwjgl", "it.unimi", "com.ibm.icu",
        "org.joml", "com.electronwill", "org.slf4j", "org.apache.logging",
        "com.sun.jna", "org.jetbrains", "kotlin.", "kotlinx.",
        "com.mojang.blaze3d", "org.anti_ad", "oshi.", "com.jcraft",
        "tv.twitch", "com.tngtech", "org.graalvm", "com.github.oshi",
        "ca.weblite", "org.lsposed", "org.openjdk"
    };

    /// <summary>
    /// 分析磁盘上的崩溃报告文件。类型由文件名与内容共同判定。
    /// </summary>
    public CrashAnalysisResult AnalyzeFile(string filePath)
    {
        var content = ReadTextWithLimit(filePath);
        var fileName = Path.GetFileName(filePath);
        return AnalyzeText(content, fileName);
    }

    /// <summary>
    /// 直接分析报告文本（测试与外部粘贴共用入口）。
    /// </summary>
    public CrashAnalysisResult AnalyzeText(string content, string fileName)
    {
        var kind = DetectKind(content, fileName);
        var result = new CrashAnalysisResult
        {
            FileName = fileName,
            Kind = kind,
            RawPreview = BuildPreview(content)
        };

        try
        {
            if (kind == CrashReportKind.JvmFatalErrorLog)
            {
                AnalyzeJvmFatalLog(content, result);
            }
            else
            {
                AnalyzeMinecraftCrashReport(content, result);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("CrashReport", $"分析崩溃报告异常: {ex.Message}");
        }

        // 兜底：一条规则都没命中时给通用建议
        if (result.Causes.Count == 0)
        {
            result.Causes.Add(new CrashCause
            {
                Category = "Unknown",
                Title = "未能匹配到已知的崩溃模式",
                Evidence = string.IsNullOrEmpty(result.ExceptionSummary)
                    ? "报告中未找到可识别的异常签名"
                    : $"异常: {result.ExceptionSummary}",
                Suggestion = result.SuspectedMods.Count > 0
                    ? "可尝试先移除/更新「可疑 Mod」列表中的 Mod 逐个排查；或携带完整报告向社区求助"
                    : "建议携带完整崩溃报告向社区求助，或使用二分法移除 Mod 排查",
                Confidence = CrashConfidence.Low
            });
        }

        return result;
    }

    // ==================== Minecraft 崩溃报告 ====================

    private void AnalyzeMinecraftCrashReport(string content, CrashAnalysisResult result)
    {
        result.CrashTime = MatchFirst(content, @"^Time:\s*(?<v>.+)$", "v");
        result.Description = MatchFirst(content, @"^Description:\s*(?<v>.+)$", "v");
        result.ExceptionSummary = ExtractExceptionSummary(content);

        var sections = ParseSections(content);
        if (sections.TryGetValue("System Details", out var systemDetails))
        {
            var details = ParseKeyValues(systemDetails);
            result.MinecraftVersion = details.GetValueOrDefault("Minecraft Version");
            result.JavaVersion = details.GetValueOrDefault("Java Version");
            result.OperatingSystem = details.GetValueOrDefault("Operating System");
            result.LoaderInfo = DetectLoader(details, content);
        }

        var causes = new List<(CrashCause Cause, int Priority)>();

        EvaluateMemoryRules(content, causes);
        EvaluateJavaVersionRules(content, causes);
        EvaluateMixinRules(content, causes);
        EvaluateFabricRules(content, causes);
        EvaluateForgeRules(content, sections, causes);
        EvaluateModCompatRules(content, causes);
        EvaluateGraphicsRules(content, causes);
        EvaluateConfigRules(content, causes);
        EvaluateWorldRules(content, causes);
        EvaluateFileAccessRules(content, causes);
        EvaluateStackOverflowRule(content, causes);
        EvaluateNativeLibraryRules(content, causes);

        result.SuspectedMods = ExtractSuspectedMods(content, sections);

        result.Causes = causes
            .OrderBy(c => c.Cause.Confidence)
            .ThenBy(c => c.Priority)
            .Select(c => c.Cause)
            .ToList();
    }

    /// <summary>异常摘要：Description 块的首行异常；缺失时取全文首个异常签名行</summary>
    private static string? ExtractExceptionSummary(string content)
    {
        var descMatch = Regex.Match(content, @"^Description:.*\r?\n(?:\r?\n)?(?<exc>[^\r\n]+)", RegexOptions.Multiline);
        if (descMatch.Success)
        {
            var line = descMatch.Groups["exc"].Value.Trim();
            if (!string.IsNullOrEmpty(line) && !line.StartsWith("A detailed walkthrough"))
            {
                return line;
            }
        }

        return MatchFirst(content,
            @"^(?<exc>(?:[\w.$]+\.)*[\w$]*(?:Exception|Error)\b[^\r\n]*)$",
            "exc", maxLines: 200);
    }

    /// <summary>解析 `-- Name --` 分节</summary>
    private static Dictionary<string, string> ParseSections(string content)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var matches = Regex.Matches(content, @"^-- (?<name>[^-][^\r\n]*?) --\s*$", RegexOptions.Multiline);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var name = matches[i].Groups["name"].Value.Trim();
            if (!sections.ContainsKey(name))
            {
                sections[name] = content.Substring(start, end - start);
            }
        }
        return sections;
    }

    /// <summary>解析 section 内的 `\tKey: Value` 键值对（仅首行值）</summary>
    private static Dictionary<string, string> ParseKeyValues(string section)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(section, @"^\t(?<key>[^:\r\n]+):\s*(?<val>[^\r\n]*)$", RegexOptions.Multiline))
        {
            var key = m.Groups["key"].Value.Trim();
            if (!dict.ContainsKey(key))
            {
                dict[key] = m.Groups["val"].Value.Trim();
            }
        }
        return dict;
    }

    private static string? DetectLoader(Dictionary<string, string> details, string content)
    {
        if (details.TryGetValue("NeoForge", out var neo) && !string.IsNullOrWhiteSpace(neo))
        {
            return $"NeoForge {neo}";
        }
        if (details.TryGetValue("Forge", out var forge) && !string.IsNullOrWhiteSpace(forge))
        {
            return forge.StartsWith("net.minecraftforge:", StringComparison.OrdinalIgnoreCase)
                ? $"Forge {forge["net.minecraftforge:".Length..]}"
                : $"Forge {forge}";
        }
        if (details.ContainsKey("Fabric Mods"))
        {
            var loader = MatchFirst(content, @"^\s*fabricloader:\s*Fabric Loader\s*(?<v>[\w.]+)\s*$", "v");
            return loader != null ? $"Fabric Loader {loader}" : "Fabric Loader";
        }
        if (content.Contains("quilt_loader", StringComparison.Ordinal) || content.Contains("Quilt Mods:", StringComparison.Ordinal))
        {
            return "Quilt Loader";
        }
        if (details.TryGetValue("ModLauncher", out _))
        {
            return "ModLauncher (Forge 系)";
        }
        return null;
    }

    // ---------------- 规则：内存 ----------------

    private static void EvaluateMemoryRules(string content, List<(CrashCause, int)> causes)
    {
        var oom = Regex.Match(content, @"OutOfMemoryError(?::\s*(?<msg>[^\r\n]+))?");
        if (!oom.Success) return;

        var msg = oom.Groups["msg"].Value;
        CrashCause cause;
        if (msg.Contains("Metaspace", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Compressed class space", StringComparison.OrdinalIgnoreCase))
        {
            cause = new CrashCause
            {
                Category = "Memory",
                Title = "Metaspace 元空间溢出（通常是 Mod 装得过多或类加载泄漏）",
                Evidence = $"OutOfMemoryError: {msg}",
                Suggestion = "精简 Mod 数量；若 Mod 不多但反复出现，怀疑某个 Mod 存在类加载泄漏，可用二分法排查",
                Confidence = CrashConfidence.High
            };
        }
        else if (msg.Contains("unable to create new native thread", StringComparison.OrdinalIgnoreCase))
        {
            cause = new CrashCause
            {
                Category = "Memory",
                Title = "系统线程耗尽（无法创建新线程）",
                Evidence = $"OutOfMemoryError: {msg}",
                Suggestion = "某个 Mod 可能在线程泄漏；关闭其他占用程序，减少 Mod，必要时升级配置",
                Confidence = CrashConfidence.High
            };
        }
        else if (msg.Contains("Direct buffer", StringComparison.OrdinalIgnoreCase))
        {
            cause = new CrashCause
            {
                Category = "Memory",
                Title = "直接内存（堆外内存）耗尽",
                Evidence = $"OutOfMemoryError: {msg}",
                Suggestion = "增加内存分配；常见于光影/渲染类 Mod，可尝试降低渲染设置或移除相关 Mod",
                Confidence = CrashConfidence.High
            };
        }
        else
        {
            cause = new CrashCause
            {
                Category = "Memory",
                Title = "内存不足（Java 堆溢出）",
                Evidence = string.IsNullOrWhiteSpace(msg) ? "OutOfMemoryError" : $"OutOfMemoryError: {msg}",
                Suggestion = "在启动器「设置 → 游戏」中调大内存分配（大型整合包建议 6-8 GB）；同时检查是否装了过多 Mod",
                Confidence = CrashConfidence.High
            };
        }
        causes.Add((cause, 10));
    }

    // ---------------- 规则：Java 版本 ----------------

    private static void EvaluateJavaVersionRules(string content, List<(CrashCause, int)> causes)
    {
        var m = Regex.Match(content, @"UnsupportedClassVersionError(?::\s*(?<msg>[^\r\n]+))?");
        if (!m.Success)
        {
            if (content.Contains("has been compiled by a more recent version of the Java Runtime", StringComparison.Ordinal))
            {
                causes.Add((new CrashCause
                {
                    Category = "Java",
                    Title = "Java 版本过低，无法加载游戏/Mod 类文件",
                    Evidence = "has been compiled by a more recent version of the Java Runtime",
                    Suggestion = JavaVersionSuggestion(),
                    Confidence = CrashConfidence.High
                }, 20));
            }
            return;
        }

        var msg = m.Groups["msg"].Value;
        var verMatch = Regex.Match(msg, @"class file version\s+(?<ver>\d+)");
        string versionHint = "";
        if (verMatch.Success && int.TryParse(verMatch.Groups["ver"].Value, out var classVer))
        {
            var javaVer = classVer - 44; // 52→8, 55→11, 61→17, 65→21
            versionHint = $"（类文件版本 {classVer} = 需要 Java {javaVer}）";
        }

        causes.Add((new CrashCause
        {
            Category = "Java",
            Title = "Java 版本与游戏/Mod 不兼容",
            Evidence = $"UnsupportedClassVersionError: {msg}{versionHint}",
            Suggestion = JavaVersionSuggestion(),
            Confidence = CrashConfidence.High
        }, 20));
    }

    private static string JavaVersionSuggestion() =>
        "在启动器「设置 → 游戏 → Java」中选择合适的 Java：Minecraft 1.20.5+ 需要 Java 21，1.17 ~ 1.20.4 需要 Java 17，1.16.5 及以下使用 Java 8";

    // ---------------- 规则：Mixin ----------------

    private static void EvaluateMixinRules(string content, List<(CrashCause, int)> causes)
    {
        var isMixinFailure =
            content.Contains("Critical injection failure", StringComparison.Ordinal) ||
            content.Contains("Mixin apply failed", StringComparison.Ordinal) ||
            content.Contains("MixinApplyError", StringComparison.Ordinal) ||
            content.Contains("MixinTargetError", StringComparison.Ordinal) ||
            content.Contains("InjectionError", StringComparison.Ordinal) ||
            content.Contains("InvalidAccessorException", StringComparison.Ordinal) ||
            Regex.IsMatch(content, @"Mixin transformation of [\w.$]+ failed");

        if (!isMixinFailure) return;

        // 提取 mixin 配置文件 / mixin 类名作为证据
        var mixinConfig = MatchFirst(content, @"(?<m>[\w.-]+\.mixins?\.json|mixins?\.[\w.-]+\.json)", "m");
        var mixinClass = MatchFirst(content, @"(?<m>[\w$]+\.)+[\w$]+Mixin\b", "m");
        var evidence = "检测到 Mixin 注入失败";
        if (mixinConfig != null) evidence += $"，相关配置: {mixinConfig}";
        if (mixinClass != null) evidence += $"，Mixin 类: {mixinClass}";

        causes.Add((new CrashCause
        {
            Category = "Mixin",
            Title = "Mixin 注入失败（两个或多个 Mod 修改了同一处游戏代码）",
            Evidence = evidence,
            Suggestion = "结合下方「可疑 Mod」定位冲突双方：更新到最新版本；若仍冲突，移除其中一个。也可在 Mod 列表中搜索 Mixin 配置名前缀对应的 Mod",
            Confidence = CrashConfidence.High
        }, 30));
    }

    // ---------------- 规则：Fabric 依赖 ----------------

    private static void EvaluateFabricRules(string content, List<(CrashCause, int)> causes)
    {
        var hasIncompatibleSet =
            content.Contains("Incompatible mod set!", StringComparison.Ordinal) ||
            content.Contains("Mod resolution encountered an incompatible mod set!", StringComparison.Ordinal);

        if (!hasIncompatibleSet) return;

        // "Could not find required mod: xxx requires {yyy} ..."
        var missing = Regex.Match(content, @"Could not find required mod:\s*(?<mod>[\w-]+)\s*requires\s*\{(?<dep>[^}]+)\}");
        if (missing.Success)
        {
            causes.Add((new CrashCause
            {
                Category = "ModLoading",
                Title = $"缺少必需的前置 Mod：{missing.Groups["dep"].Value}",
                Evidence = missing.Value.Trim(),
                Suggestion = "安装缺失的前置 Mod（注意选择与当前游戏版本匹配的分支），然后重新启动",
                Confidence = CrashConfidence.High
            }, 40));
            return;
        }

        // "Mod 'X' requires version A.B.C of mod 'Y', but only wrong version is present: ..."
        var versionConflict = Regex.Match(content,
            @"Mod\s+'(?<mod>[^']+)'\s*requires\s*(?:any version of\s*)?mod\s*'(?<dep>[^']+)'|requires version\s+(?<ver>[\w.]+)\s+of\s+(?<dep2>[\w-]+)");
        if (versionConflict.Success)
        {
            causes.Add((new CrashCause
            {
                Category = "ModLoading",
                Title = "Mod 依赖版本不满足",
                Evidence = FirstNonEmpty(versionConflict.Value.Trim(),
                    MatchFirst(content, @"^\s*(?<l>-\s*Mod\s+[^\r\n]+)$", "l") ?? ""),
                Suggestion = "按提示升级/降级对应 Mod 或其前置，使其版本落在要求区间内",
                Confidence = CrashConfidence.High
            }, 40));
            return;
        }

        causes.Add((new CrashCause
        {
            Category = "ModLoading",
            Title = "Fabric Mod 集合不兼容（依赖缺失或版本冲突）",
            Evidence = "Incompatible mod set!",
            Suggestion = "查看报告中的详细依赖提示，补齐缺失前置或调整冲突 Mod 的版本",
            Confidence = CrashConfidence.High
        }, 40));
    }

    // ---------------- 规则：Forge / NeoForge 加载 ----------------

    private static void EvaluateForgeRules(string content, Dictionary<string, string> sections, List<(CrashCause, int)> causes)
    {
        // Forge 崩溃报告为每个出问题的 Mod 生成 "-- MOD <modid> --" 节，内含 Failure message
        var modFailures = new List<string>();
        foreach (Match m in Regex.Matches(content,
            @"-- MOD (?<modid>[\w.-]+) --\s*\r?\nDetails:(?<body>.*?)(?=\r?\n-- |\z)",
            RegexOptions.Singleline))
        {
            var body = m.Groups["body"].Value;
            var failure = Regex.Match(body, @"Failure message:\s*(?<msg>[^\r\n]+)");
            var modFile = Regex.Match(body, @"Mod File:\s*(?<file>[^\r\n]+)");
            if (failure.Success)
            {
                var entry = $"{m.Groups["modid"].Value}: {failure.Groups["msg"].Value.Trim()}";
                if (modFile.Success) entry += $" ({Path.GetFileName(modFile.Groups["file"].Value.Trim())})";
                modFailures.Add(entry);
            }
        }

        if (modFailures.Count > 0)
        {
            var joined = string.Join("\n", modFailures.Take(3));
            causes.Add((new CrashCause
            {
                Category = "ModLoading",
                Title = $"Mod 加载失败：{modFailures[0].Split(':')[0]}" + (modFailures.Count > 1 ? $" 等 {modFailures.Count} 个" : ""),
                Evidence = joined,
                Suggestion = "按失败提示处理：缺失前置则安装前置，版本不符则更换对应版本的 Mod 文件",
                Confidence = CrashConfidence.High
            }, 41));
            return;
        }

        if (content.Contains("DuplicateModsFoundException", StringComparison.Ordinal) ||
            content.Contains("Duplicate mods found", StringComparison.Ordinal))
        {
            var dup = MatchFirst(content, @"DuplicateModsFoundException[^\r\n]*(?::\s*(?<m>[^\r\n]+))?", "m")
                      ?? MatchFirst(content, @"Duplicate mods found:\s*(?<m>[^\r\n]+)", "m");
            causes.Add((new CrashCause
            {
                Category = "ModLoading",
                Title = "mods 文件夹中存在重复的 Mod",
                Evidence = string.IsNullOrEmpty(dup) ? "DuplicateModsFoundException" : dup,
                Suggestion = "打开 mods 文件夹，删除同一 Mod 的多余副本（通常保留最新版本）",
                Confidence = CrashConfidence.High
            }, 41));
            return;
        }

        var missingDeps = MatchFirst(content,
            @"Missing or unsupported mandatory dependencies:\s*(?<m>[^\r\n]+)", "m");
        if (missingDeps != null || content.Contains("ModLoadingException", StringComparison.Ordinal))
        {
            var detail = missingDeps
                         ?? MatchFirst(content, @"ModLoadingException:\s*(?<m>[^\r\n]+)", "m")
                         ?? "ModLoadingException";
            causes.Add((new CrashCause
            {
                Category = "ModLoading",
                Title = "Forge/NeoForge Mod 加载失败（依赖缺失或版本不符）",
                Evidence = detail,
                Suggestion = "根据提示安装缺失的前置 Mod，或更换与当前游戏版本匹配的 Mod 版本",
                Confidence = CrashConfidence.Medium
            }, 41));
        }
    }

    // ---------------- 规则：Mod 版本错配（缺类/缺方法） ----------------

    private static void EvaluateModCompatRules(string content, List<(CrashCause, int)> causes)
    {
        var m = Regex.Match(content,
            @"(?<kind>NoClassDefFoundError|ClassNotFoundException|NoSuchMethodError|NoSuchFieldError)(?::\s*(?<cls>[^\r\n]+))?");
        if (!m.Success) return;

        var kind = m.Groups["kind"].Value;
        var cls = ExtractMissingClassName(m.Groups["cls"].Value);
        // 缺失类落在官方包 → 多为加载器/游戏版本错配；否则是 Mod 间问题
        var isOfficial = OfficialPackagePrefixes.Any(p => cls.StartsWith(p, StringComparison.Ordinal));

        var (title, suggestion) = kind switch
        {
            "NoSuchMethodError" or "NoSuchFieldError" =>
                ("Mod 调用了不存在的方法/字段（典型的 Mod 版本错配）",
                 "某个 Mod 是针对其他游戏版本或其他版本的依赖库构建的：请核对所有 Mod 是否都匹配当前 Minecraft 与加载器版本"),
            _ when isOfficial =>
                ("找不到游戏/运行时类（游戏或加载器版本不匹配）",
                 "Mod 与当前 Minecraft 版本不兼容，或加载器版本过旧：升级加载器并核对 Mod 支持的游戏版本"),
            _ =>
                ("找不到 Mod 类（前置缺失或 Mod 版本错配）",
                 "检查是否缺少前置 Mod；若前置已安装，则是 Mod 之间版本不兼容，尝试全部更新到最新"),
        };

        causes.Add((new CrashCause
        {
            Category = "ModCompat",
            Title = title,
            Evidence = $"{kind}: {cls}",
            Suggestion = suggestion,
            Confidence = CrashConfidence.Medium
        }, 50));
    }

    /// <summary>
    /// 从缺类/缺方法异常消息中提取类名。
    /// 兼容：'void com.foo.Bar.baz(...)' / 'com.foo.Bar.field' / com/foo/Bar 等形式。
    /// </summary>
    private static string ExtractMissingClassName(string raw)
    {
        var text = raw.Trim().Trim('\'', '"');
        if (text.Length == 0) return "";

        // 方法签名：取「最后一段.方法名(」之前的部分
        var methodMatch = Regex.Match(text, @"(?<cls>[\w./$]+)\.[\w$<>]+\(");
        if (methodMatch.Success)
        {
            return methodMatch.Groups["cls"].Value.Replace('/', '.');
        }

        // 普通类名/字段：取首个含包路径的 token，去掉末尾可能的字段名段
        var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        token = token.Replace('/', '.');
        var parts = token.Split('.');
        if (parts.Length > 1 && parts[^1].Length > 0 && char.IsLower(parts[^1][0]))
        {
            // 末段以小写开头 → 大概率是字段/方法名，去掉
            token = string.Join('.', parts.Take(parts.Length - 1));
        }
        return token;
    }

    // ---------------- 规则：显卡 / 显示 ----------------

    private static void EvaluateGraphicsRules(string content, List<(CrashCause, int)> causes)
    {
        string? evidence = null;
        string title;
        string suggestion;

        if (content.Contains("Pixel format not accelerated", StringComparison.Ordinal) ||
            content.Contains("GLFW error 65542", StringComparison.Ordinal))
        {
            title = "显卡不支持所需的 OpenGL（驱动过旧或为远程桌面环境）";
            evidence = "GLFW error 65542 / Pixel format not accelerated";
            suggestion = "更新显卡驱动（Intel 核显尤其注意）；远程桌面/虚拟机下请先直连本机再启动游戏";
        }
        else if (content.Contains("GLFW error 65543", StringComparison.Ordinal))
        {
            title = "显卡驱动不支持游戏要求的 OpenGL 版本";
            evidence = "GLFW error 65543";
            suggestion = "更新显卡驱动；老核显（如 Intel HD 3000 及以前）无法运行 1.17+ 原版，可加 VulkanMod 等替代渲染";
        }
        else if (Regex.IsMatch(content, @"Failed to create (?:the )?window", RegexOptions.IgnoreCase) &&
                 content.Contains("org.lwjgl", StringComparison.Ordinal))
        {
            title = "创建游戏窗口失败（显卡驱动或显示环境异常）";
            evidence = MatchFirst(content, @"Failed to create[^\r\n]*", "") ?? "";
            suggestion = "更新显卡驱动；检查是否在使用远程桌面/向日葵等远控软件，或显卡被节能策略禁用";
        }
        else
        {
            return;
        }

        causes.Add((new CrashCause
        {
            Category = "Graphics",
            Title = title,
            Evidence = evidence ?? "",
            Suggestion = suggestion,
            Confidence = CrashConfidence.High
        }, 60));
    }

    // ---------------- 规则：配置文件损坏 ----------------

    private static void EvaluateConfigRules(string content, List<(CrashCause, int)> causes)
    {
        var isConfigIssue =
            content.Contains("LoadingConfigException", StringComparison.Ordinal) ||
            content.Contains("com.electronwill.nightconfig", StringComparison.Ordinal) ||
            (content.Contains("JsonSyntaxException", StringComparison.Ordinal) &&
             (content.Contains("config", StringComparison.OrdinalIgnoreCase) ||
              content.Contains("Failed to load", StringComparison.OrdinalIgnoreCase)));

        if (!isConfigIssue) return;

        var configFile = MatchFirst(content, @"(?<f>[\w./\\-]+\.(?:toml|json|json5|cfg))", "f");
        causes.Add((new CrashCause
        {
            Category = "Config",
            Title = "Mod 配置文件损坏",
            Evidence = configFile != null ? $"疑似损坏的配置文件: {configFile}" : "检测到配置解析异常（nightconfig/Gson）",
            Suggestion = "删除（或先备份移走）游戏目录 config 下对应的配置文件，让 Mod 重新生成默认配置",
            Confidence = CrashConfidence.Medium
        }, 70));
    }

    // ---------------- 规则：世界 / 存档 ----------------

    private static void EvaluateWorldRules(string content, List<(CrashCause, int)> causes)
    {
        string? title = null;
        string? evidence = null;
        string? suggestion = null;

        if (content.Contains("Ticking block entity", StringComparison.Ordinal))
        {
            title = "存档中某个方块实体（机器/容器等）处理时崩溃";
            evidence = "Ticking block entity";
            suggestion = "从备份恢复存档；或用 MCASelector/NBT 编辑器删除出问题的方块（报告中通常有坐标）";
        }
        else if (content.Contains("Ticking entity", StringComparison.Ordinal))
        {
            title = "存档中某个实体处理时崩溃";
            evidence = "Ticking entity";
            suggestion = "从备份恢复存档；或按报告中的实体类型/坐标，用工具移除该实体；若来自某个 Mod 的生物请更新该 Mod";
        }
        else if (content.Contains("Exception generating new chunk", StringComparison.Ordinal))
        {
            title = "新区块生成时崩溃（多为世界生成类 Mod 冲突）";
            evidence = "Exception generating new chunk";
            suggestion = "检查地形/生态/结构生成类 Mod 的版本兼容性，更新或逐个移除排查";
        }
        else if (content.Contains("Loading NBT data", StringComparison.Ordinal) &&
                 (content.Contains("EOFException", StringComparison.Ordinal) ||
                  content.Contains("ZipException", StringComparison.Ordinal) ||
                  content.Contains("Unexpected end of ZLIB", StringComparison.OrdinalIgnoreCase)))
        {
            title = "存档数据损坏";
            evidence = "Loading NBT data + EOF/ZIP 异常";
            suggestion = "存档文件已损坏：从 level.dat_old 或备份恢复；排查磁盘/掉电问题";
        }

        if (title == null) return;

        causes.Add((new CrashCause
        {
            Category = "World",
            Title = title,
            Evidence = evidence!,
            Suggestion = suggestion!,
            Confidence = CrashConfidence.Medium
        }, 80));
    }

    // ---------------- 规则：文件占用 / 权限 ----------------

    private static void EvaluateFileAccessRules(string content, List<(CrashCause, int)> causes)
    {
        var occupied = Regex.IsMatch(content,
            @"(being used by another process|被另一个进程使用|另一个程序正在使用此文件|Access is denied|拒绝访问)");
        var sessionLock = content.Contains("session.lock", StringComparison.OrdinalIgnoreCase);

        if (sessionLock && occupied)
        {
            causes.Add((new CrashCause
            {
                Category = "FileAccess",
                Title = "存档被占用：同一游戏目录已在另一个游戏实例中运行",
                Evidence = "session.lock 被占用",
                Suggestion = "关闭正在运行的其他游戏实例（或其他启动器），再重新启动",
                Confidence = CrashConfidence.High
            }, 90));
            return;
        }

        if (occupied || content.Contains("AccessDeniedException", StringComparison.Ordinal))
        {
            causes.Add((new CrashCause
            {
                Category = "FileAccess",
                Title = "文件被占用或没有访问权限",
                Evidence = MatchFirst(content, @"(?<l>[^\r\n]*(?:AccessDeniedException|being used by another process|被另一个进程使用)[^\r\n]*)", "l") ?? "",
                Suggestion = "关闭杀毒软件实时扫描对游戏目录的拦截（或添加白名单）；游戏目录放在 OneDrive/网盘同步目录时请移出",
                Confidence = CrashConfidence.Medium
            }, 90));
        }
    }

    // ---------------- 规则：栈溢出 ----------------

    private static void EvaluateStackOverflowRule(string content, List<(CrashCause, int)> causes)
    {
        if (!content.Contains("StackOverflowError", StringComparison.Ordinal)) return;

        causes.Add((new CrashCause
        {
            Category = "ModCompat",
            Title = "栈溢出（多为 Mod 间的无限递归调用）",
            Evidence = "StackOverflowError",
            Suggestion = "常见于配方/数据生成类 Mod 循环引用：更新相关 Mod，或用二分法移除排查",
            Confidence = CrashConfidence.Medium
        }, 100));
    }

    // ---------------- 规则：native 库损坏 ----------------

    private static void EvaluateNativeLibraryRules(string content, List<(CrashCause, int)> causes)
    {
        var m = Regex.Match(content,
            @"Failed to load (?:the )?native library|no lwjgl\d* in java\.library\.path|UnsatisfiedLinkError(?::\s*(?<m>[^\r\n]+))?");
        if (!m.Success) return;

        causes.Add((new CrashCause
        {
            Category = "ModLoading",
            Title = "游戏运行库（LWJGL native 库）缺失或损坏",
            Evidence = m.Value.Trim(),
            Suggestion = "在启动器中重新下载/修复该游戏版本的运行库文件（libraries），然后重新启动",
            Confidence = CrashConfidence.Medium
        }, 110));
    }

    // ---------------- 可疑 Mod 提取 ----------------

    private static List<string> ExtractSuspectedMods(string content, Dictionary<string, string> sections)
    {
        var suspects = new List<string>();

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var v = value.Trim().TrimEnd('.');
            if (v.Length == 0 || v.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
            if (!suspects.Contains(v, StringComparer.OrdinalIgnoreCase))
            {
                suspects.Add(v);
            }
        }

        // 1) Forge/NeoForge 报告头的 "Suspected Mod(s): xxx (modid)"
        foreach (Match m in Regex.Matches(content, @"^Suspected Mods?:\s*(?<v>[^\r\n]+)$", RegexOptions.Multiline))
        {
            var raw = m.Groups["v"].Value.Trim();
            if (raw.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
            // 形如 "Name (modid), Version: 1.0" 或多个逗号分隔
            foreach (Match mm in Regex.Matches(raw, @"\((?<id>[\w.-]+)\)"))
            {
                Add(mm.Groups["id"].Value);
            }
            if (!Regex.IsMatch(raw, @"\([\w.-]+\)"))
            {
                Add(raw.Split(',')[0]);
            }
        }

        // 2) Mixin 配置文件名 → 前缀通常是 modid
        foreach (Match m in Regex.Matches(content, @"(?<id>[\w-]+)\.mixins?\.json"))
        {
            Add($"（Mixin）{m.Groups["id"].Value}");
        }

        // 3) 堆栈包名统计（前 150 帧内，排除官方包）
        var packageCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var frames = Regex.Matches(content, @"^\s*at\s+(?<cls>[\w.$]+)\.[\w$<>]+\(", RegexOptions.Multiline);
        var limit = Math.Min(frames.Count, 150);
        for (var i = 0; i < limit; i++)
        {
            var cls = frames[i].Groups["cls"].Value;
            if (OfficialPackagePrefixes.Any(p => cls.StartsWith(p, StringComparison.Ordinal))) continue;

            var segments = cls.Split('.');
            if (segments.Length < 2) continue;
            var key = segments[0] + "." + segments[1];
            packageCount[key] = packageCount.GetValueOrDefault(key) + 1;
        }

        // 尝试把包名与 Fabric Mods 列表对上（modid 出现在包名中 → 显示 "modid (Mod 名)"）
        var fabricMods = ParseFabricModList(content);
        foreach (var kv in packageCount.OrderByDescending(kv => kv.Value).Take(5))
        {
            var matched = fabricMods.FirstOrDefault(fm =>
                kv.Key.Contains(fm.Key.Replace('_', '.'), StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains(fm.Key.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
            Add(matched.Key != null ? $"{matched.Key} ({matched.Value})" : kv.Key);
        }

        return suspects.Take(8).ToList();
    }

    /// <summary>解析 System Details 中的 "Fabric Mods:" 列表 → modid → 显示名</summary>
    private static Dictionary<string, string> ParseFabricModList(string content)
    {
        var mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sectionMatch = Regex.Match(content, @"^\s*Fabric Mods:\s*$(?<body>.*?)(?=^\S|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);
        if (!sectionMatch.Success) return mods;

        foreach (Match m in Regex.Matches(sectionMatch.Groups["body"].Value,
            @"^\s*(?<id>[a-z0-9_-]+):\s*(?<name>.+?)\s+(?<ver>[\w.+-]+)\s*$", RegexOptions.Multiline))
        {
            var id = m.Groups["id"].Value;
            if (id is "minecraft" or "java" or "fabricloader" or "fabric-api") continue;
            if (!mods.ContainsKey(id))
            {
                mods[id] = m.Groups["name"].Value.Trim();
            }
        }
        return mods;
    }

    // ==================== JVM 致命错误日志 (hs_err) ====================

    private void AnalyzeJvmFatalLog(string content, CrashAnalysisResult result)
    {
        // #  EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=..., pid=..., tid=...
        var errorLine = MatchFirst(content,
            @"^#\s+(?<err>EXCEPTION_ACCESS_VIOLATION|EXCEPTION_STACK_OVERFLOW|EXCEPTION_INT_DIVIDE_BY_ZERO|SIGSEGV|SIGBUS|SIGILL|SIGFPE|Internal Error)[^\r\n]*",
            "err");
        result.ExceptionSummary = errorLine != null ? $"JVM 致命错误: {errorLine}" : "JVM 致命错误（native 崩溃）";
        result.Description = "Java 虚拟机崩溃（非游戏代码异常）";

        result.JavaVersion = MatchFirst(content, @"^#\s*JRE version:\s*(?<v>[^\r\n]+)$", "v");
        result.OperatingSystem = MatchFirst(content, @"^#\s*OS:\s*(?<v>[^\r\n]+)$", "v");
        result.CrashTime = MatchFirst(content, @"^#\s*date:\s*(?<v>[^\r\n]+)$", "v")
                           ?? MatchFirst(content, @"^#\s*Time:\s*(?<v>[^\r\n]+)$", "v");
        result.MinecraftVersion = MatchFirst(content, @"(?<v>1\.\d+(?:\.\d+)?)[/\\]", "v");

        // # Problematic frame:
        // # C  [ig9icd64.dll+0x123456]
        var frame = MatchFirst(content,
            @"^#\s*Problematic frame:\s*\r?\n#\s*(?<typ>\w+)\s*\[(?<mod>[^\]+0x\s]+)", "mod");
        var frameType = MatchFirst(content,
            @"^#\s*Problematic frame:\s*\r?\n#\s*(?<typ>\w+)\s*\[", "typ");

        var causes = new List<(CrashCause, int)>();

        if (content.Contains("Native memory allocation (mmap) failed", StringComparison.Ordinal) ||
            content.Contains("Native memory allocation (malloc) failed", StringComparison.Ordinal))
        {
            causes.Add((new CrashCause
            {
                Category = "Memory",
                Title = "系统内存/虚拟内存耗尽（native 内存分配失败）",
                Evidence = "Native memory allocation failed",
                Suggestion = "关闭其他占用内存的程序；增大系统虚拟内存；或降低游戏内存分配",
                Confidence = CrashConfidence.High
            }, 10));
        }

        if (frame != null)
        {
            var (brand, known) = ClassifyNativeModule(frame);
            if (known)
            {
                causes.Add((new CrashCause
                {
                    Category = "Graphics",
                    Title = $"显卡驱动崩溃（{brand}）",
                    Evidence = $"Problematic frame: {frame}，错误类型: {errorLine ?? "未知"}",
                    Suggestion = "前往显卡官网更新驱动到最新版（笔记本建议用厂商定制驱动）；若已是最新可尝试回退稳定版；核显机器确认游戏使用的是独显/正确 GPU",
                    Confidence = CrashConfidence.High
                }, 20));
            }
            else if (frame.Equals("jvm.dll", StringComparison.OrdinalIgnoreCase) ||
                     frame.Equals("libjvm.so", StringComparison.OrdinalIgnoreCase))
            {
                causes.Add((new CrashCause
                {
                    Category = "Java",
                    Title = "JVM 自身崩溃（JIT 编译或 GC 内部错误）",
                    Evidence = $"Problematic frame: {frame}",
                    Suggestion = "更换其他发行版的 Java（如 Temurin/Microsoft OpenJDK）或升级小版本；检查是否有 JVM 参数拼写错误",
                    Confidence = CrashConfidence.Medium
                }, 25));
            }
            else
            {
                causes.Add((new CrashCause
                {
                    Category = "Graphics",
                    Title = "Native 层崩溃（驱动或第三方注入组件）",
                    Evidence = $"Problematic frame: {frame}，错误类型: {errorLine ?? "未知"}",
                    Suggestion = "优先更新显卡驱动；若装了 RTSS/微星小飞机/Discord 覆盖层等注入型软件请关闭后重试",
                    Confidence = CrashConfidence.Medium
                }, 25));
            }
        }
        else if (frameType == null && causes.Count == 0)
        {
            causes.Add((new CrashCause
            {
                Category = "Unknown",
                Title = "JVM 崩溃但未记录故障位置",
                Evidence = errorLine ?? "日志中缺少 Problematic frame",
                Suggestion = "多为极端环境下的驱动/内存问题：更新显卡驱动并检查系统内存稳定性（可跑 memtest）",
                Confidence = CrashConfidence.Low
            }, 30));
        }

        result.SuspectedMods = ExtractSuspectedMods(content, new Dictionary<string, string>());
        result.Causes = causes
            .OrderBy(c => c.Item1.Confidence)
            .ThenBy(c => c.Item2)
            .Select(c => c.Item1)
            .ToList();
    }

    /// <summary>按 native 模块名识别显卡品牌</summary>
    private static (string Brand, bool Known) ClassifyNativeModule(string module)
    {
        var m = module.ToLowerInvariant();
        if (Regex.IsMatch(m, @"^ig\w*icd\w*\.dll$|^ig\d+icd\d+\.dll$|^igc64\.dll$|^igxelpicd\w*\.dll$|^igdgmm\d*\.dll$"))
            return ("Intel 核显", true);
        if (m.StartsWith("atio6axx") || m.StartsWith("atiogl") || m.StartsWith("atig6") ||
            m.StartsWith("atidxx") || m.StartsWith("amdxdna") || m.StartsWith("amdvlk"))
            return ("AMD 显卡", true);
        if (m.StartsWith("nvoglv") || m.StartsWith("nvlddmkm") || m.StartsWith("nvwgf2um") || m.StartsWith("nvcompiler"))
            return ("NVIDIA 显卡", true);
        if (m is "d3d9.dll" or "dxgi.dll" or "d3d11.dll")
            return ("DirectX/显卡", true);
        if (m.StartsWith("libnvidia") || m.StartsWith("libamdradeon") || m.StartsWith("radeonsi"))
            return ("Linux 显卡驱动", true);
        return ("", false);
    }

    // ==================== 共用工具 ====================

    private static CrashReportKind DetectKind(string content, string fileName)
    {
        if (fileName.StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase))
        {
            return CrashReportKind.JvmFatalErrorLog;
        }
        if (content.Contains("A fatal error has been detected by the Java Runtime Environment", StringComparison.Ordinal))
        {
            return CrashReportKind.JvmFatalErrorLog;
        }
        return CrashReportKind.MinecraftCrashReport;
    }

    private static string? MatchFirst(string content, string pattern, string group, int maxLines = 0)
    {
        var options = RegexOptions.Multiline;
        var input = content;
        if (maxLines > 0)
        {
            var lines = content.Split('\n');
            input = string.Join('\n', lines.Take(maxLines));
        }
        var m = Regex.Match(input, pattern, options);
        if (!m.Success) return null;
        var g = group.Length == 0 ? m.Value : m.Groups[group].Value;
        var v = g.Trim();
        return v.Length == 0 ? null : v;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string BuildPreview(string content)
    {
        var lines = content.Split('\n');
        return string.Join('\n', lines.Take(PreviewLineCount)).TrimEnd();
    }

    private static string ReadTextWithLimit(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = (int)Math.Min(stream.Length, MaxReadBytes);
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = stream.Read(buffer, read, length - read);
            if (n <= 0) break;
            read += n;
        }
        // 崩溃报告统一为 UTF-8（MC 写出）；个别系统编码残留字节容错处理
        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
