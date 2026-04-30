using System;
using Avalonia.Data.Converters;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Converters;

public class EnumToChineseTextConverter : IValueConverter
{
    public static readonly EnumToChineseTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
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

        if (value is AccountType at)
        {
            if (parameter?.ToString() == "Icon")
            {
                return at switch
                {
                    AccountType.Microsoft => "⊞",
                    AccountType.Yggdrasil => "🛡",
                    _ => "👤"
                };
            }

            return at switch
            {
                AccountType.Microsoft => "微软账户",
                AccountType.Yggdrasil => "外置登录",
                _ => "离线账户"
            };
        }

        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
