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

    private enum VersionType
    {
        Release,
        Snapshot,
        Unknown
    }

    private GameVersionNumber(string version, VersionType type, bool isNewFormat, int year, int release, int patch, int legacyMinor = 0)
    {
        _originalVersion = version;
        _type = type;
        _isNewFormat = isNewFormat;
        _year = year;
        _release = release;
        _patch = patch;
        _legacyMinor = legacyMinor;
    }

    public static GameVersionNumber Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return new GameVersionNumber(version ?? "", VersionType.Unknown, false, 0, 0, 0);

        var newReleaseMatch = Regex.Match(version, @"^(\d{2})\.(\d+)(?:\.(\d+))?$");
        if (newReleaseMatch.Success)
        {
            int year = int.Parse(newReleaseMatch.Groups[1].Value);
            int release = int.Parse(newReleaseMatch.Groups[2].Value);
            int patch = newReleaseMatch.Groups[3].Success ? int.Parse(newReleaseMatch.Groups[3].Value) : 0;
            return new GameVersionNumber(version, VersionType.Release, true, year, release, patch);
        }

        var newSnapshotMatch = Regex.Match(version, @"^(\d{2})\.(\d+)-snapshot-(\d+)$");
        if (newSnapshotMatch.Success)
        {
            int year = int.Parse(newSnapshotMatch.Groups[1].Value);
            int release = int.Parse(newSnapshotMatch.Groups[2].Value);
            int patch = int.Parse(newSnapshotMatch.Groups[3].Value);
            return new GameVersionNumber(version, VersionType.Snapshot, true, year, release, patch);
        }

        var legacyReleaseMatch = Regex.Match(version, @"^1\.(\d+)(?:\.(\d+))?(?:-.*)?$");
        if (legacyReleaseMatch.Success)
        {
            int minor = int.Parse(legacyReleaseMatch.Groups[1].Value);
            int patch = legacyReleaseMatch.Groups[2].Success ? int.Parse(legacyReleaseMatch.Groups[2].Value) : 0;
            return new GameVersionNumber(version, VersionType.Release, false, 0, 0, patch, minor);
        }

        var legacySnapshotMatch = Regex.Match(version, @"^(\d{2})w(\d{2})([a-z])$");
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

        if (_type != other._type)
        {
            if (_type == VersionType.Unknown) return -1;
            if (other._type == VersionType.Unknown) return 1;
            return _type.CompareTo(other._type);
        }

        if (_type == VersionType.Unknown)
        {
            return string.Compare(_originalVersion, other._originalVersion, StringComparison.Ordinal);
        }

        if (_isNewFormat != other._isNewFormat)
        {
            return _isNewFormat ? 1 : -1;
        }

        if (!_isNewFormat)
        {
            int c = _legacyMinor.CompareTo(other._legacyMinor);
            if (c != 0) return c;
            return _patch.CompareTo(other._patch);
        }

        int yearCmp = _year.CompareTo(other._year);
        if (yearCmp != 0) return yearCmp;

        int releaseCmp = _release.CompareTo(other._release);
        if (releaseCmp != 0) return releaseCmp;

        return _patch.CompareTo(other._patch);
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

        return Regex.IsMatch(version, @"^\d{2}\.\d+(\.\d+)?$") ||
               Regex.IsMatch(version, @"^\d{2}\.\d+-snapshot-\d+$") ||
               Regex.IsMatch(version, @"^1\.\d+(\.\d+)?") ||
               Regex.IsMatch(version, @"^\d{2}w\d{2}[a-z]$");
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
