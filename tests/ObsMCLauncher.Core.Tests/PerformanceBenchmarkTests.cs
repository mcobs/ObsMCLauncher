using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using ObsMCLauncher.Core.Services;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 性能基准测试：测量大量 Mod 和存档加载场景下的耗时和内存占用。
/// 这些测试不验证正确性，仅用于建立基准数据和识别性能瓶颈。
/// 阈值设置较为宽松，避免在不同机器上误报。
/// </summary>
public class PerformanceBenchmarkTests
{
    private static readonly string PerfRoot = Path.Combine(Path.GetTempPath(), "omcl_perf_" + Guid.NewGuid());

    private static string CreateModsDir(int count)
    {
        var dir = Path.Combine(PerfRoot, $"mods_{count}");
        Directory.CreateDirectory(dir);
        for (int i = 0; i < count; i++)
        {
            CreateFabricModJar(dir, $"mod{i:D4}.jar", $"perfmod{i}", "1.0.0", $"Perf Mod {i}");
        }
        return dir;
    }

    private static void CreateFabricModJar(string dir, string fileName, string modId, string version, string name)
    {
        var json = "{\"schemaVersion\":1,\"id\":\"" + modId + "\",\"version\":\"" + version + "\",\"name\":\"" + name + "\",\"depends\":{}}";
        var path = Path.Combine(dir, fileName);
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("fabric.mod.json");
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(json);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string CreateWorldsDir(int count)
    {
        var dir = Path.Combine(PerfRoot, $"saves_{count}");
        Directory.CreateDirectory(dir);
        for (int i = 0; i < count; i++)
        {
            var worldDir = Path.Combine(dir, $"World{i:D3}");
            Directory.CreateDirectory(worldDir);
            // 创建一个最小化的 level.dat
            CreateMinimalLevelDat(Path.Combine(worldDir, "level.dat"), $"1.20.{i % 10}");
            // 创建一些占位文件模拟世界大小
            File.WriteAllBytes(Path.Combine(worldDir, "region.dat"), new byte[1024 * (i % 100 + 1)]);
        }
        return dir;
    }

    private static void CreateMinimalLevelDat(string path, string versionId)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write((byte)10); // TAG_Compound
        WriteNbtString(writer, ""); // 根名

        writer.Write((byte)10);
        WriteNbtString(writer, "Data");
        writer.Write((byte)10);
        WriteNbtString(writer, "Version");
        writer.Write((byte)8);
        WriteNbtString(writer, "Id");
        WriteNbtString(writer, versionId);
        writer.Write((byte)0); // Version 结束
        writer.Write((byte)0); // Data 结束
        writer.Write((byte)0); // 根结束

        using var compressedMs = new MemoryStream();
        using (var gz = new GZipStream(compressedMs, CompressionLevel.Optimal, leaveOpen: false))
            gz.Write(ms.ToArray(), 0, (int)ms.Length);

        File.WriteAllBytes(path, compressedMs.ToArray());
    }

    private static void WriteNbtString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((byte)((bytes.Length >> 8) & 0xFF));
        w.Write((byte)(bytes.Length & 0xFF));
        w.Write(bytes);
    }

    [Fact]
    public void Benchmark_ModConflictDetection_100Mods()
    {
        var dir = CreateModsDir(100);
        try
        {
            // 预热
            ModConflictDetector.DetectConflicts(dir);

            var sw = Stopwatch.StartNew();
            var beforeMem = GC.GetTotalMemory(true);
            var conflicts = ModConflictDetector.DetectConflicts(dir);
            sw.Stop();
            var afterMem = GC.GetTotalMemory(false);

            // 100 个无冲突的 Mod 应在 2 秒内完成
            Assert.True(sw.ElapsedMilliseconds < 2000, $"100 Mods 检测耗时 {sw.ElapsedMilliseconds}ms 超过 2000ms");
            // 内存增长应小于 5MB
            var memGrowth = afterMem - beforeMem;
            Assert.True(memGrowth < 5 * 1024 * 1024, $"内存增长 {memGrowth / 1024}KB 超过 5MB");

            OutputBenchmarkResult("100 Mods 冲突检测", sw.ElapsedMilliseconds, memGrowth, conflicts.Count);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Benchmark_ModConflictDetection_500ModsWithConflicts()
    {
        var dir = CreateModsDir(500);
        // 添加一些冲突模组
        CreateFabricModJar(dir, "dup1.jar", "perfmod000", "2.0.0", "Dup Mod A");
        CreateFabricModJar(dir, "dup2.jar", "perfmod000", "2.0.0", "Dup Mod B");
        try
        {
            var sw = Stopwatch.StartNew();
            var conflicts = ModConflictDetector.DetectConflicts(dir);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 5000, $"500 Mods 检测耗时 {sw.ElapsedMilliseconds}ms 超过 5000ms");
            Assert.True(conflicts.Count > 0, "应检测到冲突");

            OutputBenchmarkResult("500 Mods（含冲突）冲突检测", sw.ElapsedMilliseconds, 0, conflicts.Count);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Benchmark_NbtReader_100Worlds()
    {
        var dir = CreateWorldsDir(100);
        try
        {
            var levelDatFiles = Directory.GetFiles(dir, "level.dat", SearchOption.AllDirectories);

            var sw = Stopwatch.StartNew();
            int successCount = 0;
            foreach (var file in levelDatFiles)
            {
                var version = NbtReader.ReadWorldVersionFromLevelDat(file);
                if (!string.IsNullOrEmpty(version)) successCount++;
            }
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 3000, $"100 个 level.dat 解析耗时 {sw.ElapsedMilliseconds}ms 超过 3000ms");
            Assert.Equal(100, successCount);

            OutputBenchmarkResult("100 个 level.dat 解析", sw.ElapsedMilliseconds, 0, successCount);
        }
        finally { CleanupDir(dir); }
    }

    [Fact]
    public void Benchmark_VersionRangeParsing_1000Iterations()
    {
        var ranges = new[]
        {
            "[1.0.0,2.0.0)",
            "[1.0.0,2.0.0]",
            "(1.0.0,2.0.0)",
            "[1.0.0,)",
            "[1.0.0]",
            "1.0.0",
            "[1.0.0,1.5.0),[2.0.0,3.0.0)",
            ""
        };

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            var range = new ModVersionRange(ranges[i % ranges.Length]);
            range.Assess(new ModVersion($"{i % 5}.0.0"));
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"1000 次版本范围解析耗时 {sw.ElapsedMilliseconds}ms 超过 1000ms");
        OutputBenchmarkResult("1000 次版本范围解析+评估", sw.ElapsedMilliseconds, 0, 1000);
    }

    private static void OutputBenchmarkResult(string name, long elapsedMs, long memBytes, int itemCount)
    {
        var memKb = memBytes / 1024.0;
        Console.WriteLine($"[BENCHMARK] {name}: {elapsedMs}ms, 内存增长 {memKb:F1}KB, 处理 {itemCount} 项");
    }

    private static void CleanupDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}
