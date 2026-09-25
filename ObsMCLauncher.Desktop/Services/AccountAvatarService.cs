using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 账号头像加载服务：优先从皮肤文件渲染头部头像，取不到皮肤时回退为
/// 「纯色背景 + 用户名首字符」生成的默认头像。
/// 将 UI 相关逻辑（Bitmap / 皮肤渲染 / 字体）从 ViewModel 中剥离。
/// </summary>
public static class AccountAvatarService
{
    /// <summary>默认头像的渲染边长（像素）。128 在 52px 卡片、200% 缩放显示下依然清晰。</summary>
    public const int DefaultAvatarSize = 128;

    /// <summary>
    /// 默认头像调色板：中等偏深的饱和色，色相分散。
    /// 白色字母与每个底色的对比度均 ≥ 3.3:1（粗体大字标准），浅色/深色主题下都清晰。
    /// </summary>
    private static readonly Color[] AvatarPalette =
    {
        Color.Parse("#E53935"), // 红
        Color.Parse("#D81B60"), // 玫红
        Color.Parse("#AD1457"), // 桃红
        Color.Parse("#8E24AA"), // 紫
        Color.Parse("#5E35B1"), // 深紫
        Color.Parse("#3949AB"), // 靛蓝
        Color.Parse("#1E88E5"), // 蓝
        Color.Parse("#0277BD"), // 深蓝
        Color.Parse("#00838F"), // 青
        Color.Parse("#00796B"), // 蓝绿
        Color.Parse("#2E7D32"), // 绿
        Color.Parse("#558B2F"), // 草绿
        Color.Parse("#EF6C00"), // 橙
        Color.Parse("#D84315"), // 深橙
        Color.Parse("#6D4C41"), // 棕
        Color.Parse("#455A64"), // 蓝灰
    };

    /// <summary>首字母字号相对头像边长的比例（按大写字母高 ≈ 0.45 倍边长取）。</summary>
    private const double LetterFontSizeRatio = 0.62;

    /// <summary>
    /// 加载账号皮肤头部头像（后台线程调用安全）。
    /// </summary>
    /// <param name="acc">账号</param>
    /// <param name="forceRefresh">强制重新获取皮肤（刷新操作时用）</param>
    public static async Task<Bitmap?> LoadHeadAsync(GameAccount acc, bool forceRefresh = false)
    {
        try
        {
            var skinPath = await SkinService.Instance.GetSkinPathAsync(acc, forceRefresh);
            if (!string.IsNullOrEmpty(skinPath) && File.Exists(skinPath))
            {
                return SkinHeadRenderer.GetHeadFromSkin(skinPath);
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// 生成默认头像：纯色背景 + 用户名首字符（离线账号、皮肤不可用时使用）。
    /// 背景色由用户名稳定散列选出并取模调色板 —— 同一账号每次启动颜色固定，
    /// 不同账号落到不同颜色，多个账号并排时更缤纷。
    /// <para>
    /// 内部使用 RenderTargetBitmap 与字体系统，<b>必须在 UI 线程调用</b>；
    /// 失败时返回 null，由调用方决定是否留空。
    /// </para>
    /// </summary>
    /// <param name="acc">账号（用其 Username 决定背景色与首字符）</param>
    /// <param name="size">渲染边长（像素）</param>
    public static Bitmap? CreateDefaultAvatar(GameAccount acc, int size = DefaultAvatarSize)
    {
        try
        {
            if (size <= 0) size = DefaultAvatarSize;

            var username = acc?.Username;
            var background = new SolidColorBrush(PickBackgroundColor(username));

            var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var context = target.CreateDrawingContext())
            {
                context.DrawRectangle(background, null, new Rect(0, 0, size, size));

                // MaxTextWidth 就是对齐边界（不设则 TextAlignment 不生效）
                var text = new FormattedText(
                    GetInitial(username),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    ResolveLetterTypeface(),
                    size * LetterFontSizeRatio,
                    Brushes.White)
                {
                    MaxTextWidth = size,
                    TextAlignment = TextAlignment.Center,
                };

                var origin = new Point(0, (size - text.Height) / 2);

                // 行盒居中 ≠ 字形居中：字高里含 descender 的空白，会让字母看起来偏上；
                // 用墨迹包围盒（BuildGeometry）把它校到几何中心，拿不到包围盒时退回行盒居中
                var offset = new Vector(0, 0);
                if (text.BuildGeometry(origin)?.Bounds is { Width: > 0, Height: > 0 } ink)
                {
                    offset = new Vector(
                        size / 2.0 - (ink.X + ink.Width / 2),
                        size / 2.0 - (ink.Y + ink.Height / 2));
                }

                context.DrawText(text, origin + offset);
            }

            return target;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("AccountAvatarService", $"生成默认头像失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 取用户名首个「文本元素」（不拆散代理对/组合字符）并大写；空用户名退回 '?'。
    /// </summary>
    private static string GetInitial(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "?";

        var enumerator = StringInfo.GetTextElementEnumerator(username.TrimStart());
        if (!enumerator.MoveNext()) return "?";

        return enumerator.GetTextElement().ToUpperInvariant();
    }

    /// <summary>按用户名稳定挑选背景色（同一用户名恒定，不同用户名尽量落到不同色）。</summary>
    private static Color PickBackgroundColor(string? username)
    {
        var hash = StableHash(string.IsNullOrEmpty(username) ? "?" : username);
        return AvatarPalette[hash % (uint)AvatarPalette.Length];
    }

    /// <summary>
    /// FNV-1a 32 位 + 终混洗：短字符串下低位分布仍均匀，
    /// 避免相邻用户名（Player1 / Player2）撞到同一个颜色。
    /// </summary>
    private static uint StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            hash ^= hash >> 16;
            hash *= 2246822507;
            hash ^= hash >> 13;
            hash *= 3266489909;
            hash ^= hash >> 16;
            return hash;
        }
    }

    /// <summary>
    /// 首字母字体：跟随应用当前字体（<c>GlobalFontFamily</c>，含 CJK 回退链），
    /// 取不到资源时退回框架默认字体。
    /// </summary>
    private static Typeface ResolveLetterTypeface()
    {
        var family = Application.Current?.TryFindResource("GlobalFontFamily", out var value) == true
                     && value is FontFamily resolved
            ? resolved
            : FontFamily.Default;

        return new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
    }
}
