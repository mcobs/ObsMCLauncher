using ObsMCLauncher.Core.Services;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// ModVersion 和 ModVersionRange 解析与比较的单元测试
/// </summary>
public class ModVersionRangeTests
{
    // ===== ModVersion 解析 =====

    [Theory]
    [InlineData("1.0.0", 1, 0, 0, null, true)]
    [InlineData("2.5.3", 2, 5, 3, null, true)]
    [InlineData("1.0.0-beta", 1, 0, 0, "beta", true)]
    [InlineData("1.0.0-alpha.1", 1, 0, 0, "alpha.1", true)]
    [InlineData("1.0", 1, 0, 0, null, true)]   // 缺少 patch 视为 0
    [InlineData("1", 1, 0, 0, null, true)]      // 仅 major
    [InlineData("1.0.0+build.123", 1, 0, 0, null, true)] // build metadata 被剥离
    [InlineData("1.0.0-rc.1+build.5", 1, 0, 0, "rc.1", true)]
    public void ModVersion_ParseStandardFormats(string input, int maj, int min, int pat, string? pre, bool valid)
    {
        var v = new ModVersion(input);
        Assert.Equal(valid, v.IsValid);
        Assert.Equal(maj, v.Major);
        Assert.Equal(min, v.Minor);
        Assert.Equal(pat, v.Patch);
        Assert.Equal(pre, v.PreRelease);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("abc", false)]          // 非数字
    public void ModVersion_InvalidInputs(string? input, bool expectedValid)
    {
        var v = new ModVersion(input);
        Assert.Equal(expectedValid, v.IsValid);
    }

    [Fact]
    public void ModVersion_InvalidMiddlePart()
    {
        var v = new ModVersion("1.x.0");
        // Major=1 可解析，Minor=x 不可解析退化为 0，IsValid 为 true
        Assert.True(v.IsValid);
        Assert.Equal(1, v.Major);
        Assert.Equal(0, v.Minor);
    }

    // ===== ModVersion 比较 =====

    [Theory]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.1.0", "1.0.1", 1)]
    [InlineData("1.0.0", "1.0", 0)]   // 1.0 等价于 1.0.0
    public void ModVersion_Compare_Normal(string a, string b, int expectedSign)
    {
        var va = new ModVersion(a);
        var vb = new ModVersion(b);
        var cmp = va.CompareTo(vb);
        Assert.Equal(expectedSign, Math.Sign(cmp));
    }

    [Theory]
    [InlineData("1.0.0-beta", "1.0.0", -1)]    // prerelease 低于正式版
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)] // prerelease 按字符串序比较
    [InlineData("1.0.0-beta.1", "1.0.0-beta.2", -1)]
    public void ModVersion_Compare_Prerelease(string a, string b, int expectedSign)
    {
        var va = new ModVersion(a);
        var vb = new ModVersion(b);
        Assert.Equal(expectedSign, Math.Sign(va.CompareTo(vb)));
    }

    [Fact]
    public void ModVersion_Compare_Null_ThrowsOrReturnsPositive()
    {
        var v = new ModVersion("1.0.0");
        Assert.Equal(1, v.CompareTo(null)); // null 视为更小
    }

    [Fact]
    public void ModVersion_Compare_InvalidFallsToStringComparison()
    {
        var valid = new ModVersion("1.0.0");
        var invalid = new ModVersion("abc");
        // 一方无效时退化为 Raw 字符串比较
        // "1.0.0" vs "abc": '1'(0x31) < 'a'(0x61)
        Assert.True(valid.CompareTo(invalid) < 0);
    }

    [Theory]
    [InlineData("1.0.0", "3.0.0", 2)]
    [InlineData("3.0.0", "1.0.0", 2)]
    [InlineData("1.0.0", "1.5.0", 0)] // 同主版本号差距为 0
    public void ModVersion_MajorGapWith(string a, string b, int expectedGap)
    {
        var va = new ModVersion(a);
        var vb = new ModVersion(b);
        Assert.Equal(expectedGap, va.MajorGapWith(vb));
    }

    // ===== ModVersionRange 软要求 =====

    [Fact]
    public void Range_SoftRequirement_PlainVersion()
    {
        var r = new ModVersionRange("1.0.0");
        Assert.True(r.IsSoftRequirement);
        Assert.NotNull(r.Recommended);
        Assert.Equal("1.0.0", r.Recommended!.Raw);
        // 软要求允许任何版本
        Assert.True(r.Contains(new ModVersion("99.0.0")));
    }

    [Fact]
    public void Range_SoftRequirement_Empty()
    {
        var r = new ModVersionRange("");
        Assert.True(r.IsSoftRequirement);
        Assert.Null(r.Recommended);
        Assert.True(r.Contains(new ModVersion("1.0.0")));
    }

    [Fact]
    public void Range_SoftRequirement_Null()
    {
        var r = new ModVersionRange(null);
        Assert.True(r.IsSoftRequirement);
    }

    // ===== ModVersionRange 硬性区间 - Contains =====

    [Theory]
    [InlineData("[1.0.0,2.0.0)", "1.0.0", true)]   // 下界包含
    [InlineData("[1.0.0,2.0.0)", "1.5.0", true)]
    [InlineData("[1.0.0,2.0.0)", "2.0.0", false)]  // 上界不包含
    [InlineData("[1.0.0,2.0.0)", "0.9.0", false)]  // 低于下界
    [InlineData("[1.0.0,2.0.0)", "2.1.0", false)]  // 高于上界
    public void Range_HalfOpen_Contains(string range, string version, bool expected)
    {
        var r = new ModVersionRange(range);
        Assert.Equal(expected, r.Contains(new ModVersion(version)));
    }

    [Theory]
    [InlineData("[1.0.0,2.0.0]", "2.0.0", true)]   // 闭区间上界包含
    [InlineData("[1.0.0,2.0.0]", "1.0.0", true)]
    [InlineData("[1.0.0,2.0.0]", "2.0.1", false)]
    public void Range_ClosedInterval_Contains(string range, string version, bool expected)
    {
        var r = new ModVersionRange(range);
        Assert.Equal(expected, r.Contains(new ModVersion(version)));
    }

    [Theory]
    [InlineData("(1.0.0,2.0.0)", "1.0.0", false)]  // 开区间下界不包含
    [InlineData("(1.0.0,2.0.0)", "1.5.0", true)]
    public void Range_OpenInterval_Contains(string range, string version, bool expected)
    {
        var r = new ModVersionRange(range);
        Assert.Equal(expected, r.Contains(new ModVersion(version)));
    }

    [Theory]
    [InlineData("[1.0.0,)", "0.5.0", false)]  // 无上界
    [InlineData("[1.0.0,)", "1.0.0", true)]
    [InlineData("[1.0.0,)", "99.0.0", true)]
    public void Range_NoUpperBound_Contains(string range, string version, bool expected)
    {
        var r = new ModVersionRange(range);
        Assert.Equal(expected, r.Contains(new ModVersion(version)));
    }

    [Theory]
    [InlineData("[1.0.0]", "1.0.0", true)]     // [1.0.0] 解析为 [1.0.0,) 无上限
    [InlineData("[1.0.0]", "1.0.1", true)]
    [InlineData("[1.0.0]", "0.9.0", false)]
    public void Range_ExactVersion_Contains(string range, string version, bool expected)
    {
        var r = new ModVersionRange(range);
        Assert.Equal(expected, r.Contains(new ModVersion(version)));
    }

    [Fact]
    public void Range_MultipleIntervals_Union()
    {
        // 联合区间：[1.0.0,1.5.0) 或 [2.0.0,3.0.0)
        var r = new ModVersionRange("[1.0.0,1.5.0),[2.0.0,3.0.0)");
        Assert.False(r.IsSoftRequirement);
        Assert.True(r.Contains(new ModVersion("1.2.0")));
        Assert.True(r.Contains(new ModVersion("2.5.0")));
        Assert.False(r.Contains(new ModVersion("1.7.0"))); // 两区间之间
        Assert.False(r.Contains(new ModVersion("3.0.0"))); // 上界不包含
    }

    [Fact]
    public void Range_Malformed_DoesNotThrow()
    {
        // 异常输入不应抛异常，仅解析失败
        var r1 = new ModVersionRange("[");
        var r2 = new ModVersionRange("[1.0.0");
        var r3 = new ModVersionRange("(,]");
        var r4 = new ModVersionRange("[]");
        // 以 [ 或 ( 开头的都视为硬性要求，即使区间解析为空
        Assert.False(r1.IsSoftRequirement);
        Assert.False(r2.IsSoftRequirement);
        Assert.False(r3.IsSoftRequirement);
        Assert.False(r4.IsSoftRequirement);
    }

    // ===== ModVersionRange.Assess - Severity 分级 =====

    [Fact]
    public void Assess_SoftRequirement_DeviatingFromRecommended_ReturnsInfo()
    {
        var r = new ModVersionRange("1.0.0");
        var dev = r.Assess(new ModVersion("2.0.0"));
        Assert.True(dev.InRange);
        Assert.Equal(ConflictSeverity.Info, dev.Severity);
        Assert.Equal(1, dev.MajorGap);
    }

    [Fact]
    public void Assess_SoftRequirement_MatchingRecommended_NoSeverity()
    {
        var r = new ModVersionRange("1.0.0");
        var dev = r.Assess(new ModVersion("1.0.0"));
        Assert.True(dev.InRange);
        Assert.Null(dev.Severity);
    }

    [Fact]
    public void Assess_InRange_NoSeverity()
    {
        var r = new ModVersionRange("[1.0.0,2.0.0)");
        var dev = r.Assess(new ModVersion("1.5.0"));
        Assert.True(dev.InRange);
        Assert.Null(dev.Severity);
    }

    [Theory]
    [InlineData("[2.0.0,3.0.0)", "1.0.0", ConflictSeverity.Error, true)] // 低 1 个主版本
    [InlineData("[2.0.0,3.0.0)", "1.5.0", ConflictSeverity.Error, true)] // 主版本号差距 1
    [InlineData("[2.0.0,3.0.0)", "1.9.9", ConflictSeverity.Error, true)] // 主版本号差距 1
    [InlineData("[2.0.0,3.0.0)", "2.0.0-beta", ConflictSeverity.Warning, true)] // prerelease 低于下界，主版本号相同
    public void Assess_BelowBound_SeverityByMajorGap(string range, string version, ConflictSeverity expected, bool below)
    {
        var r = new ModVersionRange(range);
        var dev = r.Assess(new ModVersion(version));
        Assert.False(dev.InRange);
        Assert.Equal(expected, dev.Severity);
        Assert.Equal(below, dev.IsBelowBound);
    }

    [Theory]
    [InlineData("[1.0.0,2.0.0)", "3.0.0", ConflictSeverity.Error, false)] // 高 1 个主版本
    [InlineData("[1.0.0,2.0.0)", "2.5.0", ConflictSeverity.Warning, false)]
    public void Assess_AboveBound_SeverityByMajorGap(string range, string version, ConflictSeverity expected, bool below)
    {
        var r = new ModVersionRange(range);
        var dev = r.Assess(new ModVersion(version));
        Assert.False(dev.InRange);
        Assert.Equal(expected, dev.Severity);
        Assert.Equal(below, dev.IsBelowBound);
    }

    [Fact]
    public void Assess_InvalidVersion_NoMajorGap_FallsToWarning()
    {
        var r = new ModVersionRange("[1.0.0,2.0.0)");
        var dev = r.Assess(new ModVersion("invalid"));
        Assert.False(dev.InRange);
        // 无效版本 MajorGap 返回 0，所以是 Warning
        Assert.Equal(ConflictSeverity.Warning, dev.Severity);
    }
}
