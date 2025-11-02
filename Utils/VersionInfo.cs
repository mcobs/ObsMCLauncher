using System;
using System.Reflection;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 全局版本信息
    /// </summary>
    public static class VersionInfo
    {
        /// <summary>
        /// 产品版本号（面向用户）
        /// 格式：主版本.次版本.修订号 (Major.Minor.Patch)
        /// 示例：1.0.0, 1.2.3, 2.0.0
        /// </summary>
        public static readonly string Version = "1.0.0";

        /// <summary>
        /// 内部版本号（构建号）
        /// 格式：日期.构建次数 (yyyyMMdd.build)
        /// 示例：20250108.1, 20250108.2
        /// </summary>
        public static readonly string BuildVersion = "20251102.1";

        /// <summary>
        /// 完整版本号（组合显示）
        /// 格式：版本号 (内部版本号)
        /// 示例：1.0.0 (20250108.1)
        /// </summary>
        public static string FullVersion => $"{Version} (Build {BuildVersion})";

        /// <summary>
        /// 短版本号（仅显示版本号）
        /// </summary>
        public static string ShortVersion => Version;

        /// <summary>
        /// 产品名称
        /// </summary>
        public static readonly string ProductName = "ObsMCLauncher";

        /// <summary>
        /// 产品完整名称
        /// </summary>
        public static readonly string FullProductName = "黑曜石MC启动器";

        /// <summary>
        /// 版本代号（可选，用于重大版本）
        /// </summary>
        public static readonly string CodeName = "Obsidian";

        /// <summary>
        /// 发布日期
        /// </summary>
        public static readonly DateTime ReleaseDate = new DateTime(2025, 11, 1);

        /// <summary>
        /// 是否为预发布版本
        /// </summary>
        public static readonly bool IsPreRelease = true;

        /// <summary>
        /// 预发布版本类型（alpha, beta, rc）
        /// </summary>
        public static readonly string PreReleaseType = "beta";

        /// <summary>
        /// 预发布版本序号
        /// </summary>
        public static readonly int PreReleaseNumber = 1;

        /// <summary>
        /// 显示版本（包含预发布信息）
        /// 示例：1.0.0-beta.1, 1.0.0 (正式版)
        /// </summary>
        public static string DisplayVersion
        {
            get
            {
                if (IsPreRelease)
                {
                    return $"{Version}-{PreReleaseType}.{PreReleaseNumber}";
                }
                return Version;
            }
        }

        /// <summary>
        /// 版本状态描述
        /// </summary>
        public static string VersionStatus
        {
            get
            {
                if (IsPreRelease)
                {
                    return PreReleaseType.ToUpper() switch
                    {
                        "ALPHA" => "内测版",
                        "BETA" => "公测版",
                        "RC" => "候选版",
                        _ => "测试版"
                    };
                }
                return "正式版";
            }
        }

        /// <summary>
        /// 获取程序集版本（从AssemblyInfo读取，作为备用）
        /// </summary>
        public static string AssemblyVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
            }
        }

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
版本号: {DisplayVersion}
内部版本号: {BuildVersion}
程序集版本: {AssemblyVersion}
发布日期: {ReleaseDate:yyyy-MM-dd}
版本状态: {VersionStatus}
版本代号: {CodeName}
            ".Trim();
        }
    }
}

