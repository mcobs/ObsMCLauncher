using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>插件描述被解析后的呈现方式</summary>
public enum PluginDescriptionKind
{
    /// <summary>没有描述</summary>
    Empty,

    /// <summary>纯文本，直接显示</summary>
    PlainText,

    /// <summary>Markdown，渲染后显示</summary>
    Markdown
}

/// <summary>
/// 插件描述解析：把市场索引 / 详情页的 <c>description</c> 字段解析为可显示的正文。
///
/// 支持三种写法（按顺序判断）：
/// <list type="number">
///   <item>是链接（<c>http(s)://</c> 开头）→ 拉取该地址的内容；GitHub 链接自动走镜像源</item>
///   <item>内容被识别为 Markdown → 按 Markdown 渲染</item>
///   <item>其余 → 纯文本直接显示</item>
/// </list>
/// </summary>
public static class PluginDescriptionResolver
{
    private static readonly Regex[] MarkdownPatterns =
    {
        new(@"^#{1,6}\s", RegexOptions.Multiline),                    // 标题
        new(@"^\s{0,3}>\s?", RegexOptions.Multiline),                 // 引用
        new(@"^\s{0,3}([-*+]|\d{1,9}\.)\s", RegexOptions.Multiline),  // 无序 / 有序列表
        new(@"```"),                                                   // 围栏代码块
        new(@"^\s{0,3}\|.+\|", RegexOptions.Multiline),               // 表格
        new(@"^\s{0,3}([-*_]\s*){3,}$", RegexOptions.Multiline),      // 分隔线
        new(@"!\[[^\]]*\]\([^)]*\)"),                                 // 图片
        new(@"\[[^\]]+\]\([^)]+\)"),                                  // 链接
        new(@"\*\*[^*\n]+\*\*"),                                      // 粗体
        new(@"`[^`\n]+`"),                                            // 行内代码
    };

    /// <summary>是否为 http/https 链接</summary>
    public static bool IsHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>粗略判断一段文本是否含 Markdown 语法</summary>
    public static bool LooksLikeMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var pattern in MarkdownPatterns)
        {
            if (pattern.IsMatch(text)) return true;
        }
        return false;
    }

    /// <summary>根据正文判断呈现方式</summary>
    public static PluginDescriptionKind DetectKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return PluginDescriptionKind.Empty;
        return LooksLikeMarkdown(text) ? PluginDescriptionKind.Markdown : PluginDescriptionKind.PlainText;
    }

    /// <summary>
    /// 解析 description：
    /// 是链接就下载内容（GitHub 链接自动走镜像源），否则直接使用原文；
    /// 再根据正文判断应该 Markdown 渲染还是纯文本显示。
    /// 网络失败时退回原文，保证详情页始终有内容。
    /// </summary>
    public static async Task<(string Text, PluginDescriptionKind Kind)> ResolveAsync(
        string? description,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (string.Empty, PluginDescriptionKind.Empty);

        var raw = description.Trim();

        if (!IsHttpUrl(raw))
            return (raw, DetectKind(raw));

        try
        {
            var url = GitHubProxyHelper.WithProxy(raw);
            var text = await httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return (text, DetectKind(text));
        }
        catch
        {
            // 拉取失败：退回链接原文，至少让用户看到来源
            return (raw, PluginDescriptionKind.PlainText);
        }
    }
}
