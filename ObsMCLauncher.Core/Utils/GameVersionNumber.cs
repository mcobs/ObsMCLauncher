using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ObsMCLauncher.Core.Utils;

public class GameVersionNumber : IComparable<GameVersionNumber>
{
    private readonly string _originalVersion;
    private readonly VersionType _type;
    private readonly bool _isNewFormat;
    private readonly int _year;
    private readonly int _release;
    private readonly int _patch;
    private readonly int _legacyMinor;
    private readonly int _preReleaseNumber;

    private enum VersionType
    {
        Release,
        Rc,
        Pre,
        Snapshot,
        Unknown
    }

    private GameVersionNumber(string version, VersionType type, bool isNewFormat, int year, int release, int patch, int legacyMinor = 0, int preReleaseNumber = 0)
    {
        _originalVersion = version;
        _type = type;
        _isNewFormat = isNewFormat;
        _year = year;
        _release = release;
        _patch = patch;
        _legacyMinor = legacyMinor;
        _preReleaseNumber = preReleaseNumber;
    }

    public static GameVersionNumber Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return new GameVersionNumber(version ?? "", VersionType.Unknown, false, 0, 0, 0);

        // 新格式正式版: 26.1, 26.1.1
        var newReleaseMatch = Regex.Match(version, @"^(\d{2})\.(\d+)(?:\.(\d+))?$", RegexOptions.IgnoreCase);
        if (newReleaseMatch.Success)
        {
            int year = int.Parse(newReleaseMatch.Groups[1].Value);
            int release = int.Parse(newReleaseMatch.Groups[2].Value);
            int patch = newReleaseMatch.Groups[3].Success ? int.Parse(newReleaseMatch.Groups[3].Value) : 0;
            return new GameVersionNumber(version, VersionType.Release, true, year, release, patch);
        }

        // 新格式预发布版: 26.1-rc.1, 26.1-pre.1, 26.1-snapshot.1
        var newPreReleaseMatch = Regex.Match(version, @"^(\d{2})\.(\d+)(?:\.(\d+))?-(rc|pre|snapshot)\.(\d+)$", RegexOptions.IgnoreCase);
        if (newPreReleaseMatch.Success)
        {
            int year = int.Parse(newPreReleaseMatch.Groups[1].Value);
            int release = int.Parse(newPreReleaseMatch.Groups[2].Value);
            int patch = newPreReleaseMatch.Groups[3].Success ? int.Parse(newPreReleaseMatch.Groups[3].Value) : 0;
            string typeStr = newPreReleaseMatch.Groups[4].Value.ToLowerInvariant();
            int preReleaseNum = int.Parse(newPreReleaseMatch.Groups[5].Value);

            VersionType type = typeStr switch
            {
                "rc" => VersionType.Rc,
                "pre" => VersionType.Pre,
                "snapshot" => VersionType.Snapshot,
                _ => VersionType.Unknown
            };
            return new GameVersionNumber(version, type, true, year, release, patch, 0, preReleaseNum);
        }

        // 旧格式正式版: 1.21.4, 1.21
        var legacyReleaseMatch = Regex.Match(version, @"^1\.(\d+)(?:\.(\d+))?$", RegexOptions.IgnoreCase);
        if (legacyReleaseMatch.Success)
        {
            int minor = int.Parse(legacyReleaseMatch.Groups[1].Value);
            int patch = legacyReleaseMatch.Groups[2].Success ? int.Parse(legacyReleaseMatch.Groups[2].Value) : 0;
            return new GameVersionNumber(version, VersionType.Release, false, 0, 0, patch, minor);
        }

        // 旧格式预发布版: 1.21.4-rc.1, 1.21.4-pre.1, 1.21-rc.1
        var legacyPreReleaseMatch = Regex.Match(version, @"^1\.(\d+)(?:\.(\d+))?-(rc|pre|snapshot)\.(\d+)$", RegexOptions.IgnoreCase);
        if (legacyPreReleaseMatch.Success)
        {
            int minor = int.Parse(legacyPreReleaseMatch.Groups[1].Value);
            int patch = legacyPreReleaseMatch.Groups[2].Success ? int.Parse(legacyPreReleaseMatch.Groups[2].Value) : 0;
            string typeStr = legacyPreReleaseMatch.Groups[3].Value.ToLowerInvariant();
            int preReleaseNum = int.Parse(legacyPreReleaseMatch.Groups[4].Value);

            VersionType type = typeStr switch
            {
                "rc" => VersionType.Rc,
                "pre" => VersionType.Pre,
                "snapshot" => VersionType.Snapshot,
                _ => VersionType.Unknown
            };
            return new GameVersionNumber(version, type, false, 0, 0, patch, minor, preReleaseNum);
        }

        // 旧格式快照: 25w14a
        var legacySnapshotMatch = Regex.Match(version, @"^(\d{2})w(\d{2})([a-z])$", RegexOptions.IgnoreCase);
        if (legacySnapshotMatch.Success)
        {
            int year = int.Parse(legacySnapshotMatch.Groups[1].Value);
            int week = int.Parse(legacySnapshotMatch.Groups[2].Value);
            char letter = legacySnapshotMatch.Groups[3].Value[0];
            return new GameVersionNumber(version, VersionType.Snapshot, false, year, week, letter - 'a');
        }

        return new GameVersionNumber(version, VersionType.Unknown, false, 0, 0, 0);
    }

    public int CompareTo(GameVersionNumber? other)
    {
        if (other == null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        // 未知版本始终排在最后
        if (_type == VersionType.Unknown && other._type == VersionType.Unknown)
            return string.Compare(_originalVersion, other._originalVersion, StringComparison.Ordinal);
        if (_type == VersionType.Unknown) return -1;
        if (other._type == VersionType.Unknown) return 1;

        // 新旧格式分开处理
        if (_isNewFormat != other._isNewFormat)
            return _isNewFormat ? 1 : -1;

        // 先按版本号比较
        int versionCmp;
        if (!_isNewFormat)
        {
            versionCmp = CompareLegacyVersions(other);
        }
        else
        {
            versionCmp = CompareNewFormatVersions(other);
        }

        if (versionCmp != 0) return versionCmp;

        // 版本号相同，按类型排序：Release > Rc > Pre > Snapshot
        return _type.CompareTo(other._type);
    }

    private int CompareLegacyVersions(GameVersionNumber other)
    {
        // 对于旧格式，需要将快照(25w14a)与正式版(1.21.x)映射到同一尺度
        // 快照用 _year(年份)/_release(周数)，正式版用 _legacyMinor(次版本号)/_patch

        bool thisIsSnapshot = _type == VersionType.Snapshot && _year > 0;
        bool otherIsSnapshot = other._type == VersionType.Snapshot && other._year > 0;

        // 年份到次版本号的映射 (用于将快照映射到对应的正式版)
        // 25wxx -> 1.21.x, 24wxx -> 1.21.x 或 1.20.x, 23wxx -> 1.20.x 等
        int thisMajor = thisIsSnapshot ? MapYearToMinorVersion(_year) : _legacyMinor;
        int otherMajor = otherIsSnapshot ? MapYearToMinorVersion(other._year) : other._legacyMinor;

        int cmp = thisMajor.CompareTo(otherMajor);
        if (cmp != 0) return cmp;

        // 同一次版本号下，比较次级版本
        // 快照用周数，正式版用patch
        int thisMinor = thisIsSnapshot ? _release : _patch;
        int otherMinor = otherIsSnapshot ? other._release : other._patch;

        cmp = thisMinor.CompareTo(otherMinor);
        if (cmp != 0) return cmp;

        // 如果都是预发布类型，比较预发布号
        if (_preReleaseNumber > 0 || other._preReleaseNumber > 0)
        {
            return other._preReleaseNumber.CompareTo(_preReleaseNumber);
        }

        return 0;
    }

    private int CompareNewFormatVersions(GameVersionNumber other)
    {
        // 新格式：先比较年份，再比较 release，再比较 patch
        int cmp = _year.CompareTo(other._year);
        if (cmp != 0) return cmp;

        cmp = _release.CompareTo(other._release);
        if (cmp != 0) return cmp;

        cmp = _patch.CompareTo(other._patch);
        if (cmp != 0) return cmp;

        // 如果都是预发布类型，比较预发布号
        if (_preReleaseNumber > 0 || other._preReleaseNumber > 0)
        {
            return other._preReleaseNumber.CompareTo(_preReleaseNumber);
        }

        return 0;
    }

    private static int MapYearToMinorVersion(int year)
    {
        // 将年份映射到对应的次版本号
        // 2025 (25) -> 21 (1.21.x)
        // 2024 (24) -> 21 或 20，这里简化处理，24年上半年对应1.20，下半年对应1.21
        // 2023 (23) -> 20
        // 2022 (22) -> 19
        // 2021 (21) -> 18 或 17
        // 2020 (20) -> 16
        return year switch
        {
            >= 25 => 21,
            24 => 20,
            23 => 20,
            22 => 19,
            21 => 17,
            20 => 16,
            19 => 15,
            18 => 14,
            17 => 13,
            16 => 12,
            15 => 10,
            14 => 8,
            _ => year
        };
    }

    public override string ToString() => _originalVersion;

    public override bool Equals(object? obj)
    {
        if (obj is GameVersionNumber other)
            return CompareTo(other) == 0;
        return false;
    }

    public override int GetHashCode() => _originalVersion.GetHashCode();
}

public class MinecraftVersionComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        bool xIsSpecial = IsSpecialGroup(x);
        bool yIsSpecial = IsSpecialGroup(y);

        if (xIsSpecial && !yIsSpecial) return 1;
        if (!xIsSpecial && yIsSpecial) return -1;
        if (xIsSpecial && yIsSpecial)
            return string.Compare(x, y, StringComparison.Ordinal);

        var vx = GameVersionNumber.Parse(x);
        var vy = GameVersionNumber.Parse(y);

        return vx.CompareTo(vy);
    }

    private bool IsSpecialGroup(string version)
    {
        return version == "未知版本" || version == "其他版本";
    }
}

public static class VersionUtils
{
    public static bool IsMinecraftVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var lowerVersion = version.ToLowerInvariant();
        if (lowerVersion.Contains("forge") ||
            lowerVersion.Contains("fabric") ||
            lowerVersion.Contains("quilt") ||
            lowerVersion.Contains("neoforge") ||
            lowerVersion.Contains("liteloader"))
        {
            return false;
        }

        return Regex.IsMatch(version, @"^\d{2}\.\d+(\.\d+)?(-rc\.\d+|-pre\.\d+|-snapshot\.\d+)?$", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(version, @"^1\.\d+(\.\d+)?(-rc\.\d+|-pre\.\d+|-snapshot\.\d+)?$", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(version, @"^\d{2}w\d{2}[a-z]$", RegexOptions.IgnoreCase);
    }

    public static string ExtractMinecraftVersion(IEnumerable<string> versions)
    {
        var mcVersions = versions
            .Where(IsMinecraftVersion)
            .OrderByDescending(v => v, new MinecraftVersionComparer())
            .ToList();

        return mcVersions.FirstOrDefault() ?? string.Empty;
    }

    public static List<string> ExtractAllMinecraftVersions(IEnumerable<string> versions)
    {
        return versions
            .Where(IsMinecraftVersion)
            .Distinct()
            .OrderByDescending(v => v, new MinecraftVersionComparer())
            .ToList();
    }

    public static List<string> FilterAndSortVersions(IEnumerable<string> versions)
    {
        return versions
            .Where(IsMinecraftVersion)
            .Distinct()
            .OrderByDescending(v => v, new MinecraftVersionComparer())
            .ToList();
    }
}
