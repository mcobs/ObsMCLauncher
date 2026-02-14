using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ObsMCLauncher.Core.Utils;

public class GameVersionNumber : IComparable<GameVersionNumber>
{
    private readonly string _originalVersion;
    private readonly VersionType _type;
    private readonly int _major;
    private readonly int _minor;
    private readonly int _patch;

    private enum VersionType
    {
        Release,
        Snapshot,
        Unknown
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

        var releaseMatch = Regex.Match(version, @"^1\.(\d+)(?:\.(\d+))?(?:-.*)?$");
        if (releaseMatch.Success)
        {
            int minor = int.Parse(releaseMatch.Groups[1].Value);
            int patch = releaseMatch.Groups[2].Success ? int.Parse(releaseMatch.Groups[2].Value) : 0;
            return new GameVersionNumber(version, VersionType.Release, 1, minor, patch);
        }

        var snapshotMatch = Regex.Match(version, @"^(\d{2})w(\d{2})([a-z])$");
        if (snapshotMatch.Success)
        {
            int year = int.Parse(snapshotMatch.Groups[1].Value);
            int week = int.Parse(snapshotMatch.Groups[2].Value);
            char letter = snapshotMatch.Groups[3].Value[0];
            return new GameVersionNumber(version, VersionType.Snapshot, year, week, letter - 'a');
        }

        return new GameVersionNumber(version, VersionType.Unknown, 0, 0, 0);
    }

    public int CompareTo(GameVersionNumber? other)
    {
        if (other == null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        if (_type != other._type)
        {
            return _type.CompareTo(other._type);
        }

        if (_type == VersionType.Unknown)
        {
            return string.Compare(_originalVersion, other._originalVersion, StringComparison.Ordinal);
        }

        int c = _major.CompareTo(other._major);
        if (c != 0) return c;

        c = _minor.CompareTo(other._minor);
        if (c != 0) return c;

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

        return Regex.IsMatch(version, @"^1\.\d+(\.\d+)?") ||
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
