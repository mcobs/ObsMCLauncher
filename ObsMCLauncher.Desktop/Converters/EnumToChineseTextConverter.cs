using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Svg.Skia;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Converters;

public class EnumToChineseTextConverter : IValueConverter, IMultiValueConverter
{
    public static readonly EnumToChineseTextConverter Instance = new();
    private static readonly string IconBase = "avares://ObsMCLauncher.Desktop/Assets/AccountIcons/";

    /// <summary>
    /// 账号类型图标源缓存：SvgSource 按 (类型, 主题) 缓存，
    /// 避免每次绑定求值都重新读取资源并做主题换色（列表项/菜单项/主题切换时高频触发）。
    /// </summary>
    private static readonly ConcurrentDictionary<(AccountType Type, ThemeVariant Theme), SvgSource?> IconSourceCache = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is AccountType at)
        {
            if (parameter?.ToString() == "IconPath")
            {
                return LoadAccountIcon(at);
            }

            if (parameter?.ToString() == "Icon")
            {
                return GetFallbackIcon(at);
            }

            return GetAccountTypeText(at);
        }

        if (value is DirectoryLocation dl)
        {
            return dl switch
            {
                DirectoryLocation.AppData => OperatingSystem.IsWindows() ? "%APPDATA%\\.minecraft（默认）"
                    : OperatingSystem.IsMacOS() ? "~/Library/Application Support/minecraft（默认）"
                    : "~/.minecraft（默认）",
                DirectoryLocation.RunningDirectory => "运行目录\\.minecraft",
                DirectoryLocation.Custom => "自定义路径",
                _ => dl.ToString()
            };
        }

        if (value is GameDirectoryType gdt)
        {
            return gdt switch
            {
                GameDirectoryType.RootFolder => "关闭 - 所有版本共享mods文件夹",
                GameDirectoryType.VersionFolder => "开启 - 每个版本使用独立mods文件夹",
                _ => gdt.ToString()
            };
        }

        if (value is DownloadSource ds)
        {
            return ds switch
            {
                DownloadSource.Official => "官方源",
                DownloadSource.BMCLAPI => "BMCLAPI 镜像",
                DownloadSource.MCBBS => "MCBBS（已弃用）",
                DownloadSource.Custom => "自定义（已弃用）",
                _ => ds.ToString()
            };
        }

        if (value is MirrorSourceMode msm)
        {
            return msm switch
            {
                MirrorSourceMode.PreferMirror => "优先镜像源",
                MirrorSourceMode.OfficialOnly => "只使用官方源",
                _ => msm.ToString()
            };
        }

        if (value is UpdateChannel uc)
        {
            return uc switch
            {
                UpdateChannel.Stable => "正式版",
                UpdateChannel.Beta => "测试版",
                UpdateChannel.RC => "预发布版",
                UpdateChannel.Preview => "预览版",
                _ => uc.ToString()
            };
        }

        return value?.ToString();
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is AccountType at)
        {
            if (parameter?.ToString() == "IconPath")
            {
                return LoadAccountIcon(at);
            }

            if (parameter?.ToString() == "Icon")
            {
                return GetFallbackIcon(at);
            }

            return GetAccountTypeText(at);
        }

        return null;
    }

    private static object? LoadAccountIcon(AccountType at)
    {
        var theme = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        var source = IconSourceCache.GetOrAdd((at, theme), key => LoadIconSource(key.Type, key.Theme));
        if (source == null)
        {
            return GetFallbackIcon(at);
        }

        return new SvgImage { Source = source };
    }

    private static SvgSource? LoadIconSource(AccountType at, ThemeVariant theme)
    {
        var path = at switch
        {
            AccountType.Microsoft => IconBase + "microsoft.svg",
            AccountType.Yggdrasil => IconBase + "yggdrasil.svg",
            _ => IconBase + "offline.svg"
        };

        try
        {
            var uri = new Uri(path);

            if (at != AccountType.Microsoft)
            {
                using var reader = new StreamReader(AssetLoader.Open(uri));
                var svgContent = reader.ReadToEnd();
                svgContent = SvgThemeHelper.ReplaceCurrentColor(svgContent, theme);

                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
                return SvgSource.LoadFromStream(ms);
            }
            else
            {
                return SvgSource.LoadFromStream(AssetLoader.Open(uri));
            }
        }
        catch
        {
            // fallback
        }

        return null;
    }

    private static string GetFallbackIcon(AccountType at)
    {
        return at switch
        {
            AccountType.Microsoft => "⊞",
            AccountType.Yggdrasil => "🛡",
            _ => "👤"
        };
    }

    private static string GetAccountTypeText(AccountType at)
    {
        return at switch
        {
            AccountType.Microsoft => "微软账户",
            AccountType.Yggdrasil => "外置登录",
            _ => "离线账户"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}