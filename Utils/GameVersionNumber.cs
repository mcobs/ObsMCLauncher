using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// Minecraft游戏版本号比较器
    /// </summary>
    public class GameVersionNumber : IComparable<GameVersionNumber>
    {
        private readonly string _originalVersion;
        private readonly VersionType _type;
        private readonly int _major;
        private readonly int _minor;
        private readonly int _patch;
        
        private enum VersionType
        {
            Release,      // 1.x.x格式
            Snapshot,     // xxwxxa格式  
            Unknown       // 其他格式
        }

        private GameVersionNumber(string version, VersionType type, int major, int minor, int patch)
        {
            _originalVersion = version;
            _type = type;
            _major = major;
            _minor = minor;
            _patch = patch;
        }

        public static GameVersionNumber Parse(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return new GameVersionNumber(version ?? "", VersionType.Unknown, 0, 0, 0);

            // 尝试解析Release版本（1.x 或 1.x.x）
            var releaseMatch = Regex.Match(version, @"^1\.(\d+)(?:\.(\d+))?(?:-.*)?$");
            if (releaseMatch.Success)
            {
                int minor = int.Parse(releaseMatch.Groups[1].Value);
                int patch = releaseMatch.Groups[2].Success ? int.Parse(releaseMatch.Groups[2].Value) : 0;
                return new GameVersionNumber(version, VersionType.Release, 1, minor, patch);
            }

            // 尝试解析Snapshot版本（例如：23w31a）
            var snapshotMatch = Regex.Match(version, @"^(\d{2})w(\d{2})([a-z])$");
            if (snapshotMatch.Success)
            {
                int year = int.Parse(snapshotMatch.Groups[1].Value);
                int week = int.Parse(snapshotMatch.Groups[2].Value);
                char letter = snapshotMatch.Groups[3].Value[0];
                
                // 使用年份、周数和字母作为版本号
                return new GameVersionNumber(version, VersionType.Snapshot, year, week, letter - 'a');
            }

            // 无法识别的版本
            return new GameVersionNumber(version, VersionType.Unknown, 0, 0, 0);
        }

        public int CompareTo(GameVersionNumber? other)
        {
            if (other == null) return 1;
            if (ReferenceEquals(this, other)) return 0;

            // 不同类型的版本比较
            if (_type != other._type)
            {
                // Release > Snapshot > Unknown
                return _type.CompareTo(other._type);
            }

            // 相同类型的版本比较
            if (_type == VersionType.Unknown)
            {
                return string.Compare(_originalVersion, other._originalVersion, StringComparison.Ordinal);
            }

            // 比较major版本
            int c = _major.CompareTo(other._major);
            if (c != 0) return c;

            // 比较minor版本
            c = _minor.CompareTo(other._minor);
            if (c != 0) return c;

            // 比较patch版本
            return _patch.CompareTo(other._patch);
        }

        public override string ToString()
        {
            return _originalVersion;
        }

        public override bool Equals(object? obj)
        {
            if (obj is GameVersionNumber other)
                return CompareTo(other) == 0;
            return false;
        }

        public override int GetHashCode()
        {
            return _originalVersion.GetHashCode();
        }
    }

    /// <summary>
    /// Minecraft版本比较器（用于排序）
    /// </summary>
    public class MinecraftVersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // 处理特殊分组
            bool xIsSpecial = IsSpecialGroup(x);
            bool yIsSpecial = IsSpecialGroup(y);

            if (xIsSpecial && !yIsSpecial) return 1;  // 特殊组排后面
            if (!xIsSpecial && yIsSpecial) return -1;
            if (xIsSpecial && yIsSpecial) 
                return string.Compare(x, y, StringComparison.Ordinal);

            // 使用GameVersionNumber进行比较
            var vx = GameVersionNumber.Parse(x);
            var vy = GameVersionNumber.Parse(y);

            return vx.CompareTo(vy);
        }

        private bool IsSpecialGroup(string version)
        {
            return version == "未知版本" || version == "其他版本";
        }
    }

    /// <summary>
    /// 版本工具类
    /// </summary>
    public static class VersionUtils
    {
        /// <summary>
        /// 判断是否是纯Minecraft版本号
        /// </summary>
        public static bool IsMinecraftVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return false;

            // 排除包含模组加载器标识的版本字符串
            var lowerVersion = version.ToLower();
            if (lowerVersion.Contains("forge") ||
                lowerVersion.Contains("fabric") ||
                lowerVersion.Contains("quilt") ||
                lowerVersion.Contains("neoforge") ||
                lowerVersion.Contains("liteloader"))
            {
                return false;
            }

            // 检查是否符合Minecraft版本格式
            // 1.x 或 1.x.x 或 xxwxxa (snapshot)
            return Regex.IsMatch(version, @"^1\.\d+(\.\d+)?") || 
                   Regex.IsMatch(version, @"^\d{2}w\d{2}[a-z]$");
        }

        /// <summary>
        /// 从版本列表中提取第一个纯Minecraft版本（按最新排序）
        /// </summary>
        public static string ExtractMinecraftVersion(IEnumerable<string> versions)
        {
            // 提取所有Minecraft版本并排序，返回最新的
            var mcVersions = versions
                .Where(IsMinecraftVersion)
                .OrderByDescending(v => v, new MinecraftVersionComparer())
                .ToList();
            
            return mcVersions.FirstOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// 从版本列表中提取所有纯Minecraft版本
        /// </summary>
        public static List<string> ExtractAllMinecraftVersions(IEnumerable<string> versions)
        {
            return versions
                .Where(IsMinecraftVersion)
                .Distinct()
                .OrderByDescending(v => v, new MinecraftVersionComparer())
                .ToList();
        }

        /// <summary>
        /// 过滤并排序Minecraft版本列表
        /// </summary>
        public static List<string> FilterAndSortVersions(IEnumerable<string> versions)
        {
            return versions
                .Where(IsMinecraftVersion)
                .Distinct()
                .OrderByDescending(v => v, new MinecraftVersionComparer())
                .ToList();
        }
    }
}

