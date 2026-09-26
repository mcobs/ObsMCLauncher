using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ObsMCLauncher.Core.Services;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// `inheritsFrom` 合并的回归测试。
///
/// 背景（真实事故，2026-09-26）：把整合包导出再导入后游戏**启动瞬间退出、退出码 1 且没有崩溃报告**。
/// 根因是 <c>MergeInheritedVersion</c> 里对 `arguments` 的合并写成了
/// 「只有子版本 arguments 为 null 才继承父版本」——而 Fabric / Forge 安装器生成的子版本 JSON
/// 自带 `arguments.jvm = ["-DFabricMcEmu=…"]`、`game = []`，于是父版本的
/// `--username / --accessToken / --gameDir / --assetIndex` 等游戏参数与
/// `-Djava.library.path` 等 JVM 参数全被丢掉，JVM 拿不到必要参数直接退出。
/// </summary>
public class InheritedVersionMergeTests : IDisposable
{
    private readonly string _gameDir;

    public InheritedVersionMergeTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "omcl-inherit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "versions"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_gameDir))
                Directory.Delete(_gameDir, true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    private void WriteVersion(string versionId, string json)
    {
        var dir = Path.Combine(_gameDir, "versions", versionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{versionId}.json"), json);
    }

    /// <summary>调用 Core 的 internal 合并方法。</summary>
    private static GameLauncher.VersionInfo Merge(string gameDir, string childId, string childJson)
    {
        var child = JsonSerializer.Deserialize<GameLauncher.VersionInfo>(
            childJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var method = typeof(GameLauncher).GetMethod(
            "MergeInheritedVersion", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (GameLauncher.VersionInfo)method.Invoke(null, new object[] { gameDir, childId, child })!;
    }

    /// <summary>arguments 元素反序列化后是 JsonElement，转成字符串方便断言。</summary>
    private static List<string> AsStrings(System.Collections.Generic.List<object>? arguments)
        => arguments?.Select(a => a switch
        {
            System.Text.Json.JsonElement e => e.ValueKind == System.Text.Json.JsonValueKind.String
                ? e.GetString() ?? ""
                : e.GetRawText(),
            _ => a?.ToString() ?? ""
        }).ToList() ?? new List<string>();

    private const string ParentJson = """
    {
      "id": "1.18.2",
      "mainClass": "net.minecraft.client.main.Main",
      "assetIndex": { "id": "1.18" },
      "libraries": [ { "name": "com.mojang:brigadier:1.0.18" } ],
      "arguments": {
        "game": [ "--username", "${auth_player_name}", "--version", "${version_name}",
                  "--gameDir", "${game_directory}", "--assetsDir", "${assets_root}",
                  "--assetIndex", "${assets_index_name}", "--accessToken", "${auth_access_token}",
                  "--uuid", "${auth_uuid}" ],
        "jvm": [ "-Djava.library.path=${natives_directory}", "-cp", "${classpath}" ]
      }
    }
    """;

    /// <summary>Fabric 安装器生成的子版本 JSON：jvm 有内容、game 是空数组。</summary>
    private const string ChildJson = """
    {
      "id": "Fabulously Optimized 1.0.0",
      "inheritsFrom": "1.18.2",
      "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
      "arguments": { "game": [], "jvm": [ "-DFabricMcEmu=net.minecraft.client.main.Main" ] },
      "libraries": [ { "name": "net.fabricmc:fabric-loader:0.19.3" } ]
    }
    """;

    [Fact]
    public void ParentGameArgumentsSurviveWhenChildHasEmptyGameArguments()
    {
        WriteVersion("1.18.2", ParentJson);

        var merged = Merge(_gameDir, "Fabulously Optimized 1.0.0", ChildJson);

        var game = AsStrings(merged.Arguments!.Game);
        // 少了这些，JVM 起来就没参数可解析 → 立刻退出且不留崩溃报告
        foreach (var required in new[] { "--username", "--accessToken", "--uuid", "--gameDir", "--assetsDir", "--assetIndex", "--version" })
            Assert.Contains(required, game);
    }

    [Fact]
    public void ParentJvmArgumentsSurviveWhenChildHasOwnJvmArguments()
    {
        WriteVersion("1.18.2", ParentJson);

        var merged = Merge(_gameDir, "Fabulously Optimized 1.0.0", ChildJson);

        var jvm = AsStrings(merged.Arguments!.Jvm);
        Assert.Contains("-Djava.library.path=${natives_directory}", jvm);
        Assert.Contains("-DFabricMcEmu=net.minecraft.client.main.Main", jvm);
    }

    [Fact]
    public void ParentArgumentsComeBeforeChildArguments()
    {
        WriteVersion("1.18.2", ParentJson);

        var merged = Merge(_gameDir, "Fabulously Optimized 1.0.0", ChildJson);

        var jvm = AsStrings(merged.Arguments!.Jvm);
        Assert.True(jvm.IndexOf("-Djava.library.path=${natives_directory}")
                    < jvm.IndexOf("-DFabricMcEmu=net.minecraft.client.main.Main"),
            "JVM 参数必须「父在前、子在后」");
    }

    [Fact]
    public void ParentMainClassAndLibrariesStillWinLoseAppropriately()
    {
        WriteVersion("1.18.2", ParentJson);

        var merged = Merge(_gameDir, "Fabulously Optimized 1.0.0", ChildJson);

        // mainClass 用子版本的（加载器入口），父版本的库被并进来（同 key 时以子版本为准）
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", merged.MainClass);
        var libraryNames = merged.Libraries!.Select(l => l.Name).ToList();
        Assert.Contains("net.fabricmc:fabric-loader:0.19.3", libraryNames);
        Assert.Contains("com.mojang:brigadier:1.0.18", libraryNames);
        Assert.Equal("1.18", merged.AssetIndex!.Id);
    }

    [Fact]
    public void ChildArgumentsAreKeptWhenParentHasNone()
    {
        WriteVersion("1.18.2", """
        {
          "id": "1.18.2",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": { "id": "1.18" },
          "libraries": []
        }
        """);

        var merged = Merge(_gameDir, "Fabulously Optimized 1.0.0", ChildJson);

        var jvm = AsStrings(merged.Arguments!.Jvm);
        Assert.Contains("-DFabricMcEmu=net.minecraft.client.main.Main", jvm);
    }

    [Fact]
    public void ParentJsonInsideChildVersionFolderIsStillFound()
    {
        // PCL 的约定：父版本 JSON 直接放在子版本目录里（整合包导出常见），必须也能解析到
        var childDir = Path.Combine(_gameDir, "versions", "Fabulously Optimized 1.0.0");
        Directory.CreateDirectory(childDir);
        File.WriteAllText(Path.Combine(childDir, "1.18.2.json"), ParentJson);

        var merged = Merge(_gameDir, "Fabulously Optimized 1.0.0", ChildJson);

        Assert.Contains("--username", AsStrings(merged.Arguments!.Game));
    }
}

/// <summary>
/// 启动时的缺失库检查：natives classifier 包缺失必须算**必需**并触发下载，
/// 不能归到"可选"里（否则永远没人去下，natives 目录一直是空的）。
/// </summary>
public class MissingLibraryDetectionTests : IDisposable
{
    private readonly string _gameDir;

    public MissingLibraryDetectionTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "omcl-missing-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "libraries"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_gameDir))
                Directory.Delete(_gameDir, true);
        }
        catch
        {
            // 清理失败不影响断言
        }

        // 静态缓存里可能记着别的东西，无碍
        GC.KeepAlive(typeof(GameLauncher));
    }

    private static GameLauncher.VersionInfo BuildLwjglVersion()
        => new()
        {
            MainClass = "net.minecraft.client.main.Main",
            Libraries = new[]
            {
                new GameLauncher.Library
                {
                    Name = "org.lwjgl:lwjgl:3.2.2",
                    Natives = new Dictionary<string, string> { ["windows"] = "natives-windows" },
                    Downloads = new GameLauncher.LibraryDownloads
                    {
                        Artifact = new GameLauncher.Artifact
                        {
                            Path = "org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2.jar",
                            Size = 1
                        },
                        Classifiers = new Dictionary<string, GameLauncher.Artifact>
                        {
                            ["natives-windows"] = new GameLauncher.Artifact
                            {
                                Path = "org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2-natives-windows.jar",
                                Size = 1
                            }
                        }
                    }
                }
            }
        };

    private static (List<string> required, List<string> optional) GetMissing(string gameDir, GameLauncher.VersionInfo version)
    {
        var method = typeof(GameLauncher).GetMethod(
            "GetMissingLibraries", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = method.Invoke(null, new object[] { gameDir, version })!;
        return ((List<string>, List<string>))result;
    }

    [Fact]
    public void MissingNativesJarCountsAsRequired()
    {
        // 主 jar 在（1 字节，与声明大小一致），只缺 natives classifier
        var mainJar = Path.Combine(_gameDir, "libraries", "org", "lwjgl", "lwjgl", "3.2.2", "lwjgl-3.2.2.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(mainJar)!);
        File.WriteAllBytes(mainJar, new byte[1]);

        var (required, optional) = GetMissing(_gameDir, BuildLwjglVersion());

        Assert.Contains("org.lwjgl:lwjgl:3.2.2", required);
        Assert.DoesNotContain("org.lwjgl:lwjgl:3.2.2", optional);
    }

    [Fact]
    public void PresentNativesJarIsNotReportedMissing()
    {
        var dir = Path.Combine(_gameDir, "libraries", "org", "lwjgl", "lwjgl", "3.2.2");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "lwjgl-3.2.2.jar"), new byte[1]);
        File.WriteAllBytes(Path.Combine(dir, "lwjgl-3.2.2-natives-windows.jar"), new byte[1]);

        var (required, optional) = GetMissing(_gameDir, BuildLwjglVersion());

        Assert.DoesNotContain("org.lwjgl:lwjgl:3.2.2", required);
        Assert.DoesNotContain("org.lwjgl:lwjgl:3.2.2", optional);
    }
}

/// <summary>
/// 库合并的去重键必须保留版本：1.18.2 的 JSON 里 LWJGL 同时有 3.2.1（仅 osx）与 3.2.2（非 osx），
/// 按 group:artifact 去重会把 3.2.2 挤掉，Windows 上就一个 lwjgl 都进不了 classpath。
/// </summary>
public class LibraryMergeDedupTests : IDisposable
{
    private readonly string _gameDir;

    public LibraryMergeDedupTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "omcl-libmerge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "versions"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_gameDir))
                Directory.Delete(_gameDir, true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    private void WriteVersion(string versionId, string json)
    {
        var dir = Path.Combine(_gameDir, "versions", versionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{versionId}.json"), json);
    }

    private const string ParentJson = """
    {
      "id": "1.18.2",
      "mainClass": "net.minecraft.client.main.Main",
      "libraries": [
        { "name": "org.lwjgl:lwjgl:3.2.1", "rules": [ { "action": "allow", "os": { "name": "osx" } } ] },
        { "name": "org.lwjgl:lwjgl:3.2.2", "rules": [ { "action": "allow" }, { "action": "disallow", "os": { "name": "osx" } } ] },
        { "name": "org.lwjgl:lwjgl:3.2.2", "rules": [ { "action": "allow" }, { "action": "disallow", "os": { "name": "osx" } } ],
          "natives": { "windows": "natives-windows", "linux": "natives-linux", "osx": "natives-macos" } }
      ]
    }
    """;

    private const string ChildJson = """
    {
      "id": "My Pack",
      "inheritsFrom": "1.18.2",
      "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
      "libraries": [ { "name": "net.fabricmc:fabric-loader:0.19.3" } ]
    }
    """;

    [Fact]
    public void BothLibraryVersionsSurviveTheMerge()
    {
        WriteVersion("1.18.2", ParentJson);

        var method = typeof(GameLauncher).GetMethod(
            "MergeInheritedVersion", BindingFlags.NonPublic | BindingFlags.Static)!;

        var child = JsonSerializer.Deserialize<GameLauncher.VersionInfo>(
            ChildJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var merged = (GameLauncher.VersionInfo)method.Invoke(
            null, new object[] { _gameDir, "My Pack", child })!;

        var names = merged.Libraries!.Select(l => l.Name).ToList();

        // 关键：3.2.2 不能被 3.2.1 "去重"掉，否则非 osx 平台上没有 lwjgl 可用
        Assert.Contains("org.lwjgl:lwjgl:3.2.2", names);
        Assert.Contains("org.lwjgl:lwjgl:3.2.1", names);
        Assert.Contains("net.fabricmc:fabric-loader:0.19.3", names);

        // natives 变体（同名但走 classifier）也要保留
        Assert.Equal(2, names.Count(n => n == "org.lwjgl:lwjgl:3.2.2"));
    }
}
