using System;
using System.Collections.Generic;

namespace ObsMCLauncher.Core.Utils;

/// <summary>
/// 版本号比较工具：语义化版本（SemVer）语义，忽略预发布 / 构建后缀，缺失段按 0 处理。
/// 例：<c>1.1.0-beta.1</c> 与 <c>1.1.0</c> 视为相等（后缀不参与比较）；<c>1.10.0</c> &gt; <c>1.9.9</c>。
/// </summary>
public static class VersionCompare
{
    /// <summary>比较两个版本号：a &lt; b 返回负数，相等返回 0，a &gt; b 返回正数。</summary>
    public static int Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        var len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }

    /// <summary>version &gt;= minimum</summary>
    public static bool IsAtLeast(string? version, string? minimum) => Compare(version, minimum) >= 0;

    /// <summary>version &lt;= maximum（maximum 为空时返回 true，即不设上限）</summary>
    public static bool IsAtMost(string? version, string? maximum)
        => string.IsNullOrWhiteSpace(maximum) || Compare(version, maximum) <= 0;

    private static int[] Parse(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return Array.Empty<int>();

        var head = v.Trim();
        if (head.StartsWith("v", StringComparison.OrdinalIgnoreCase)) head = head[1..];
        head = head.Split('-', '+')[0];

        var parts = new List<int>();
        foreach (var part in head.Split('.'))
        {
            if (int.TryParse(part.Trim(), out var n))
                parts.Add(n);
            else
                break;
        }
        return parts.ToArray();
    }
}
