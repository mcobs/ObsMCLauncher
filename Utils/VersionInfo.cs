using System;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 版本状态枚举
    /// </summary>
    public enum VersionStatus
    {
        /// <summary>
        /// 测试版
        /// </summary>
        Testing,
        
        /// <summary>
        /// 预发布版本
        /// </summary>
        PreRelease,
        
        /// <summary>
        /// 正式版
        /// </summary>
        Release
    }

    /// <summary>
    /// 全局版本信息
    /// </summary>
    public static class VersionInfo
    {
        /// <summary>
        /// 产品版本号
        /// 格式：主版本.次版本.修订版本 (Major.Minor.Patch)
        /// 示例：1.0.0, 1.2.3, 2.0.0
        /// </summary>
        public static readonly string Version = "1.0.0";

        /// <summary>
        /// 版本代号（可选，用于重大版本）
        /// </summary>
        public static readonly string CodeName = "GrassBlock";

        /// <summary>
        /// 版本状态
        /// </summary>
        public static readonly VersionStatus Status = VersionStatus.Testing;

        /// <summary>
        /// 产品名称
        /// </summary>
        public static readonly string ProductName = "ObsMCLauncher";

        /// <summary>
        /// 产品完整名称
        /// </summary>
        public static readonly string FullProductName = "黑曜石MC启动器";

        /// <summary>
        /// 发布日期
        /// </summary>
        public static readonly DateTime ReleaseDate = new DateTime(2026, 01, 17);

        /// <summary>
        /// 短版本号（仅显示版本号）
        /// </summary>
        public static string ShortVersion => Version;

        /// <summary>
        /// 显示版本（包含版本号和状态信息）
        /// 示例：1.0.0 (预发布版本), 1.0.0 (正式版)
        /// </summary>
        public static string DisplayVersion
        {
            get
            {
                var statusText = Status switch
                {
                    VersionStatus.Testing => "测试版",
                    VersionStatus.PreRelease => "预发布版本",
                    VersionStatus.Release => "正式版",
                    _ => "未知"
                };
                return $"{Version} ({statusText})";
            }
        }

        /// <summary>
        /// 版本状态描述（中文）
        /// </summary>
        public static string VersionStatusText => Status switch
        {
            VersionStatus.Testing => "测试版",
            VersionStatus.PreRelease => "预发布版本",
            VersionStatus.Release => "正式版",
            _ => "未知"
        };

        /// <summary>
        /// 版权信息
        /// </summary>
        public static string Copyright => $"© {ReleaseDate.Year} {ProductName}";

        /// <summary>
        /// 获取详细版本信息（用于日志、调试）
        /// </summary>
        public static string GetDetailedVersionInfo()
        {
            return $@"
产品名称: {FullProductName}
版本号: {Version}
版本状态: {VersionStatusText}
版本代号: {CodeName}
发布日期: {ReleaseDate:yyyy-MM-dd}
            ".Trim();
        }
    }
}

