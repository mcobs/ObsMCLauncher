using System.IO;
using System.IO.Compression;
using System.Text;
using ObsMCLauncher.Core.Services;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// NbtReader 的单元测试，覆盖正常、边界和异常情况
/// </summary>
public class NbtReaderTests
{
    /// <summary>
    /// 构造一个模拟的 level.dat NBT 字节流
    /// </summary>
    private static byte[] BuildLevelDatBytes(string rootName = "", bool compress = true,
        string? versionId = null, bool includeData = true)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);

        // 根 TAG_Compound
        writer.Write((byte)10);
        WriteNbtString(writer, rootName);

        if (includeData)
        {
            // Data 子 compound
            writer.Write((byte)10);
            WriteNbtString(writer, "Data");

            // Version 子 compound
            writer.Write((byte)10);
            WriteNbtString(writer, "Version");

            if (versionId is not null)
            {
                writer.Write((byte)8); // TAG_String
                WriteNbtString(writer, "Id");
                WriteNbtString(writer, versionId);
            }

            // 一些干扰字段
            writer.Write((byte)3); // TAG_Int
            WriteNbtString(writer, "Snapshot");
            writer.Write(0);

            writer.Write((byte)0); // Version compound 结束

            // Data compound 中的其他字段（干扰）
            writer.Write((byte)8); // TAG_String
            WriteNbtString(writer, "LevelName");
            WriteNbtString(writer, "TestWorld");

            writer.Write((byte)0); // Data compound 结束
        }

        writer.Write((byte)0); // 根 compound 结束

        var raw = ms.ToArray();
        if (!compress) return raw;

        using var compressedMs = new MemoryStream();
        using (var gz = new GZipStream(compressedMs, CompressionLevel.Optimal, leaveOpen: false))
            gz.Write(raw, 0, raw.Length);
        return compressedMs.ToArray();
    }

    private static void WriteNbtString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        // NBT 使用大端序，先写高位字节再写低位字节
        w.Write((byte)((bytes.Length >> 8) & 0xFF));
        w.Write((byte)(bytes.Length & 0xFF));
        w.Write(bytes);
    }

    [Fact]
    public void ReadVersion_FromCompressedLevelDat()
    {
        var bytes = BuildLevelDatBytes(versionId: "1.20.4");
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("1.20.4", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_FromUncompressedLevelDat()
    {
        // 未压缩的 NBT（魔数非 0x1F 0x8B）
        var bytes = BuildLevelDatBytes(versionId: "1.19.2", compress: false);
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("1.19.2", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_MissingVersionCompound_ReturnsEmpty()
    {
        // 没有 Version compound 的 level.dat
        var bytes = BuildLevelDatBytes(versionId: null);
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_EmptyFile_ReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, new byte[0]);
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_NotExists_ReturnsEmpty()
    {
        var v = NbtReader.ReadWorldVersionFromLevelDat(Path.Combine(Path.GetTempPath(), "nonexistent_" + System.Guid.NewGuid() + ".dat"));
        Assert.Equal("", v);
    }

    [Fact]
    public void ReadVersion_TruncatedStream_ReturnsEmpty()
    {
        // 构造一个被严重截断的 GZip 流（仅保留极少字节）
        var bytes = BuildLevelDatBytes(versionId: "1.0.0");
        // 截断到极短，确保 NBT 结构无法完整解析
        Array.Resize(ref bytes, Math.Min(20, bytes.Length));
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            // 不应抛异常，应优雅返回空字符串
            Assert.Equal("", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_InvalidRootTag_ReturnsEmpty()
    {
        // 根标签类型不是 TAG_Compound(10)
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write((byte)8); // TAG_String 作为根标签（错误）
        WriteNbtString(writer, "root");
        WriteNbtString(writer, "value");

        using var compressedMs = new MemoryStream();
        using (var gz = new GZipStream(compressedMs, CompressionLevel.Optimal, leaveOpen: false))
            gz.Write(ms.ToArray(), 0, (int)ms.Length);

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, compressedMs.ToArray());
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_OnlyGzipHeader_ReturnsEmpty()
    {
        // 仅 GZip 头部，无 NBT 内容
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: false))
        { /* 不写入任何内容 */ }

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, ms.ToArray());
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_WithSpecialCharsInId()
    {
        // 版本字符串包含特殊字符（预发布版本）
        var bytes = BuildLevelDatBytes(versionId: "1.20.4-rc.1+build.7");
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("1.20.4-rc.1+build.7", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_WithUnicodeId()
    {
        // 测试 UTF-8 编码的版本字符串
        var bytes = BuildLevelDatBytes(versionId: "1.20.4-测试");
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("1.20.4-测试", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadVersion_DeepNestedStructure()
    {
        // 测试嵌套很深的 compound 结构，Version 在较深的位置
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write((byte)10); // 根
        WriteNbtString(writer, "");

        // 一层无关 compound 包裹
        for (int i = 0; i < 5; i++)
        {
            writer.Write((byte)10);
            WriteNbtString(writer, $"Layer{i}");
        }

        // Data compound 在最内层
        writer.Write((byte)10);
        WriteNbtString(writer, "Data");

        writer.Write((byte)10);
        WriteNbtString(writer, "Version");

        writer.Write((byte)8);
        WriteNbtString(writer, "Id");
        WriteNbtString(writer, "1.21.0");

        writer.Write((byte)0); // Version 结束
        writer.Write((byte)0); // Data 结束

        // 关闭所有 Layer compound
        for (int i = 0; i < 5; i++)
            writer.Write((byte)0);

        writer.Write((byte)0); // 根结束

        using var compressedMs = new MemoryStream();
        using (var gz = new GZipStream(compressedMs, CompressionLevel.Optimal, leaveOpen: false))
            gz.Write(ms.ToArray(), 0, (int)ms.Length);

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, compressedMs.ToArray());
        try
        {
            // 由于 NbtReader 只查找根 compound 下的 Data.Version，深层嵌套的 Data 不会被找到
            // 这是预期行为：Data 必须在根 compound 直接子级
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("", v);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FindStringByKey_FindsFirstMatch()
    {
        // 在同一 compound 中有多个同名 string，应返回第一个
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write((byte)10); // 根
        WriteNbtString(writer, "");

        writer.Write((byte)10);
        WriteNbtString(writer, "Data");
        writer.Write((byte)10);
        WriteNbtString(writer, "Version");

        writer.Write((byte)8);
        WriteNbtString(writer, "Id");
        WriteNbtString(writer, "first");

        writer.Write((byte)8);
        WriteNbtString(writer, "Id");
        WriteNbtString(writer, "second");

        writer.Write((byte)0); // Version 结束
        writer.Write((byte)0); // Data 结束
        writer.Write((byte)0); // 根结束

        using var compressedMs = new MemoryStream();
        using (var gz = new GZipStream(compressedMs, CompressionLevel.Optimal, leaveOpen: false))
            gz.Write(ms.ToArray(), 0, (int)ms.Length);

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, compressedMs.ToArray());
        try
        {
            var v = NbtReader.ReadWorldVersionFromLevelDat(path);
            Assert.Equal("first", v);
        }
        finally { File.Delete(path); }
    }
}
