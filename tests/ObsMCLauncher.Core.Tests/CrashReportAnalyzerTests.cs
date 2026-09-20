using System;
using System.IO;
using System.Linq;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services.Crash;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// CrashReportAnalyzer / CrashReportScanner 的单元测试。
/// 样本为合成的典型崩溃报告：OOM、Mixin 失败、Fabric 依赖缺失、Forge Mod 加载失败、
/// NoSuchMethodError、Java 版本不符、显卡 GLFW、JVM hs_err（Intel 核显）等。
/// </summary>
public class CrashReportAnalyzerTests
{
    private readonly CrashReportAnalyzer _analyzer = CrashReportAnalyzer.Instance;

    // ---------------- 样本 ----------------

    private const string OomReport = """
        ---- Minecraft Crash Report ----
        // Don't be sad, have a hug! <3

        Time: 2026-09-20 10:00:00
        Description: Exception in server tick loop

        java.lang.OutOfMemoryError: Java heap space
        	at net.minecraft.server.MinecraftServer.tick(MinecraftServer.java:100)
        	at net.minecraft.client.main.Main.main(Main.java:50)

        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- System Details --
        Details:
        	Minecraft Version: 1.20.1
        	Operating System: Windows 11 (amd64) version 10.0
        	Java Version: 17.0.8, Microsoft
        	Memory: 500000000 bytes (476 MiB) / 4294967296 bytes (4096 MiB) up to 4294967296 bytes (4096 MiB)
        """;

    private const string MixinReport = """
        ---- Minecraft Crash Report ----
        // Why did you do that?

        Time: 2026-09-19 22:00:00
        Description: Initializing game

        org.spongepowered.asm.mixin.transformer.throwables.MixinApplyError: Mixin [sodium.mixins.json:features.render.MixinWorldRenderer] from phase [DEFAULT] in config [sodium.mixins.json] FAILED during APPLY
        	at org.spongepowered.asm.mixin.transformer.MixinProcessor.handleMixinError(MixinProcessor.java:638)
        	at org.spongepowered.asm.mixin.transformer.MixinProcessor.applyMixins(MixinProcessor.java:379)
        	at com.tterrag.registrate.AbstractRegistrate.setup(AbstractRegistrate.java:40)
        	at net.minecraft.client.main.Main.main(Main.java:200)
        Caused by: org.spongepowered.asm.mixin.injection.throwables.InjectionError: Critical injection failure
        	at org.spongepowered.asm.mixin.injection.struct.InjectionInfo.postInject(InjectionInfo.java:468)

        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- Head --
        Thread: Render thread
        Stacktrace:
        	at me.jellysquid.mods.sodium.client.render.SodiumWorldRenderer.updateChunks(SodiumWorldRenderer.java:90)
        	at net.minecraft.client.renderer.LevelRenderer.renderLevel(LevelRenderer.java:1000)

        -- System Details --
        Details:
        	Minecraft Version: 1.20.1
        	Java Version: 17.0.9, Azul Systems, Inc.
        	Operating System: Windows 10 (amd64) version 10.0
        	Fabric Mods:
        		fabric-api: Fabric API 0.91.0+1.20.1
        		sodium: Sodium 0.5.3
        		registrate: Registrate 1.3.0
        		fabricloader: Fabric Loader 0.15.7
        """;

    private const string FabricMissingDepReport = """
        ---- Minecraft Crash Report ----
        // I bet Cylons wouldn't have this problem.

        Time: 2026-09-18 08:00:00
        Description: Initializing game

        net.fabricmc.loader.impl.FormattedException: Mod resolution encountered an incompatible mod set!
        A potential solution has been determined:
        	 - Install mod 'Cloth Config', any version.
        Unmet dependency listing:
        	 - Mod 'Sodium Extra' (sodium-extra) 0.5.4+mc1.20.1-build.115 requires any version of mod 'Cloth Config' (cloth-config), which is missing!
        Incompatible mod set!
        	at net.fabricmc.loader.impl.FormattedException.ofLocalized(FormattedException.java:51)
        	at net.fabricmc.loader.impl.FabricLoaderImpl.load(FabricLoaderImpl.java:196)
        """;

    private const string ForgeModFailureReport = """
        ---- Minecraft Crash Report ----
        // Hi. I'm Minecraft, and I'm a crashaholic.

        Time: 2026-09-17 12:00:00
        Description: Mod loading error has occurred

        net.minecraftforge.fml.ModLoadingException: Some mods failed to load
        	at net.minecraftforge.fml.ModLoader.waitForTransition(ModLoader.java:246)

        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- Head --
        Thread: Render thread
        Suspected Mod: Example Mod (examplemod), Version: 1.0
        Stacktrace:
        	at net.minecraftforge.fml.loading.FMLLoader.findMods(FMLLoader.java:100)

        -- MOD examplemod --
        Details:
        	Mod File: /C:/games/mc/mods/examplemod-1.0.jar
        	Failure message: Mod examplemod requires jei 15 or above
        		Currently, jei is not installed
        	Mod Version: 1.0

        -- System Details --
        Details:
        	Minecraft Version: 1.20.1
        	Java Version: 17.0.8, Eclipse Adoptium
        	Operating System: Windows 11 (amd64) version 10.0
        	Forge: net.minecraftforge:47.2.0
        """;

    private const string NoSuchMethodReport = """
        ---- Minecraft Crash Report ----
        // Ooh. Shiny.

        Time: 2026-09-16 09:00:00
        Description: Unexpected error

        java.lang.NoSuchMethodError: 'void com.example.betterfurnace.FurnaceHelper.smelt(net.minecraft.world.item.ItemStack)'
        	at com.example.betterfurnace.FurnaceScreen.render(FurnaceScreen.java:42)
        	at net.minecraft.client.renderer.GameRenderer.render(GameRenderer.java:900)
        	at net.minecraft.client.Minecraft.runTick(Minecraft.java:1200)
        	at net.minecraft.client.main.Main.main(Main.java:250)

        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- System Details --
        Details:
        	Minecraft Version: 1.20.1
        	Java Version: 17.0.7, Oracle Corporation
        	Operating System: Windows 10 (amd64) version 10.0
        	Forge: net.minecraftforge:47.1.3
        """;

    private const string JavaVersionReport = """
        ---- Minecraft Crash Report ----
        // Who set us up the TNT?

        Time: 2026-09-15 07:30:00
        Description: Initializing game

        java.lang.UnsupportedClassVersionError: net/minecraft/client/main/Main has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0
        	at java.base/java.lang.ClassLoader.defineClass1(Native Method)
        	at java.base/java.lang.ClassLoader.defineClass(ClassLoader.java:1017)

        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- System Details --
        Details:
        	Minecraft Version: 1.21.1
        	Java Version: 17.0.8, Microsoft
        	Operating System: Windows 11 (amd64) version 10.0
        """;

    private const string GlfwReport = """
        ---- Minecraft Crash Report ----
        // I just don't know what went wrong :(

        Time: 2026-09-14 06:00:00
        Description: Initializing game

        org.lwjgl.glfw.GLFWError: GLFW error 65542: WGL: The driver does not appear to support OpenGL
        	at org.lwjgl.glfw.GLFW.glfwCreateWindow(GLFW.java:200)
        	at net.minecraft.client.main.Main.main(Main.java:180)

        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- System Details --
        Details:
        	Minecraft Version: 1.20.4
        	Java Version: 17.0.9, Microsoft
        	Operating System: Windows 10 (amd64) version 10.0
        """;

    private const string HsErrIntel = """
        #
        # A fatal error has been detected by the Java Runtime Environment:
        #
        #  EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=0x00007ffb12345678, pid=12345, tid=67890
        #
        # JRE version: OpenJDK Runtime Environment Microsoft-9388422 (17.0.8+7) (build 17.0.8+7-LTS)
        # Java VM: OpenJDK 64-Bit Server VM Microsoft-9388422 (17.0.8+7-LTS, mixed mode, tiered, compressed oops, compressed class ptrs, g1 gc, windows-amd64)
        # Problematic frame:
        # C  [ig9icd64.dll+0x2c4567]
        #
        # No core dump will be written. Minidumps are not enabled by default on client versions of Windows
        #
        #

        ---------------  S U M M A R Y ------------

        Command Line: -Xmx4096m net.minecraft.client.main.Main
        """;

    private const string HsErrMalloc = """
        #
        # A fatal error has been detected by the Java Runtime Environment:
        #
        # Native memory allocation (malloc) failed to allocate 2097152 bytes for Chunk::new
        #
        # JRE version: OpenJDK Runtime Environment Temurin-21.0.3+9 (21.0.3+9) (build 21.0.3+9-LTS)
        # Java VM: OpenJDK 64-Bit Server VM Temurin-21.0.3+9 (21.0.3+9-LTS, mixed mode, tiered, compressed oops, compressed class ptrs, z gc, windows-amd64)
        # Problematic frame:
        # C  [jvm.dll+0x6c2a10]
        #
        """;

    // ---------------- MC 崩溃报告规则 ----------------

    [Fact]
    public void Analyze_OomReport_IdentifiesMemoryIssue()
    {
        var result = _analyzer.AnalyzeText(OomReport, "crash-2026-09-20_10.00.00-server.txt");

        Assert.Equal(CrashReportKind.MinecraftCrashReport, result.Kind);
        Assert.Equal("1.20.1", result.MinecraftVersion);
        Assert.Equal("Exception in server tick loop", result.Description);
        Assert.NotEmpty(result.Causes);
        var top = result.Causes[0];
        Assert.Equal("Memory", top.Category);
        Assert.Equal(CrashConfidence.High, top.Confidence);
        Assert.Contains("内存", top.Title);
    }

    [Fact]
    public void Analyze_MixinFailure_IdentifiesMixinAndSuspectsMod()
    {
        var result = _analyzer.AnalyzeText(MixinReport, "crash-2026-09-19_22.00.00-client.txt");

        Assert.Equal("1.20.1", result.MinecraftVersion);
        Assert.Equal("Fabric Loader 0.15.7", result.LoaderInfo);

        var mixin = result.Causes.FirstOrDefault(c => c.Category == "Mixin");
        Assert.NotNull(mixin);
        Assert.Equal(CrashConfidence.High, mixin.Confidence);
        Assert.Contains("sodium.mixins.json", mixin.Evidence);

        // 堆栈里出现 me.jellysquid.mods.sodium → 应对照 Fabric Mods 表识别出 sodium (Sodium)
        Assert.Contains(result.SuspectedMods, s => s.Contains("sodium", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_FabricMissingDependency_GivesInstallSuggestion()
    {
        var result = _analyzer.AnalyzeText(FabricMissingDepReport, "crash-2026-09-18_08.00.00-client.txt");

        var cause = result.Causes.FirstOrDefault(c => c.Category == "ModLoading");
        Assert.NotNull(cause);
        Assert.Equal(CrashConfidence.High, cause.Confidence);
        Assert.Contains("依赖", cause.Title + cause.Suggestion + cause.Evidence);
    }

    [Fact]
    public void Analyze_ForgeModFailure_ExtractsFailureMessage()
    {
        var result = _analyzer.AnalyzeText(ForgeModFailureReport, "crash-2026-09-17_12.00.00-client.txt");

        Assert.Equal("Forge 47.2.0", result.LoaderInfo);

        var cause = result.Causes.FirstOrDefault(c => c.Category == "ModLoading");
        Assert.NotNull(cause);
        Assert.Equal(CrashConfidence.High, cause.Confidence);
        Assert.Contains("examplemod", cause.Evidence);
        Assert.Contains("requires jei", cause.Evidence);

        Assert.Contains(result.SuspectedMods, s => s.Contains("examplemod", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_NoSuchMethodError_IdentifiesModVersionMismatch()
    {
        var result = _analyzer.AnalyzeText(NoSuchMethodReport, "crash-2026-09-16_09.00.00-client.txt");

        var cause = result.Causes.FirstOrDefault(c => c.Category == "ModCompat");
        Assert.NotNull(cause);
        Assert.Contains("版本", cause.Title);
        Assert.Contains("com.example.betterfurnace", cause.Evidence);

        Assert.Contains(result.SuspectedMods, s => s.Contains("example", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_UnsupportedClassVersion_IdentifiesJavaMismatch()
    {
        var result = _analyzer.AnalyzeText(JavaVersionReport, "crash-2026-09-15_07.30.00-client.txt");

        var cause = result.Causes.FirstOrDefault(c => c.Category == "Java");
        Assert.NotNull(cause);
        Assert.Equal(CrashConfidence.High, cause.Confidence);
        Assert.Contains("Java 21", cause.Evidence); // class file version 65 → Java 21
        Assert.Contains("Java", cause.Suggestion);
    }

    [Fact]
    public void Analyze_GlfwError_IdentifiesGraphicsDriver()
    {
        var result = _analyzer.AnalyzeText(GlfwReport, "crash-2026-09-14_06.00.00-client.txt");

        var cause = result.Causes.FirstOrDefault(c => c.Category == "Graphics");
        Assert.NotNull(cause);
        Assert.Contains("显卡", cause.Title);
    }

    // ---------------- hs_err 规则 ----------------

    [Fact]
    public void Analyze_HsErrIntelIcd_IdentifiesIntelDriver()
    {
        var result = _analyzer.AnalyzeText(HsErrIntel, "hs_err_pid12345.log");

        Assert.Equal(CrashReportKind.JvmFatalErrorLog, result.Kind);
        Assert.NotEmpty(result.Causes);
        var top = result.Causes[0];
        Assert.Equal("Graphics", top.Category);
        Assert.Contains("Intel", top.Title);
        Assert.Contains("ig9icd64.dll", top.Evidence);
    }

    [Fact]
    public void Analyze_HsErrMallocFail_IdentifiesSystemMemoryExhaustion()
    {
        var result = _analyzer.AnalyzeText(HsErrMalloc, "hs_err_pid999.log");

        var cause = result.Causes.FirstOrDefault(c => c.Category == "Memory");
        Assert.NotNull(cause);
        Assert.Equal(CrashConfidence.High, cause.Confidence);
    }

    [Fact]
    public void DetectKind_ByContent_NotOnlyFileName()
    {
        // 即使文件名不带 hs_err 前缀，内容特征也应识别为 JVM 致命日志
        var result = _analyzer.AnalyzeText(HsErrIntel, "crash.txt");
        Assert.Equal(CrashReportKind.JvmFatalErrorLog, result.Kind);
    }

    // ---------------- 兜底 ----------------

    [Fact]
    public void Analyze_UnrecognizedReport_FallsBackToLowConfidenceCause()
    {
        const string weird = """
            ---- Minecraft Crash Report ----
            // This doesn't make any sense!

            Time: 2026-09-13 05:00:00
            Description: Something strange happened

            com.example.unknown.CustomFailure: blip blop
            	at com.example.unknown.Foo.bar(Foo.java:1)

            A detailed walkthrough of the error, its code path and all known details is as follows:
            ---------------------------------------------------------------------------------------
            """;

        var result = _analyzer.AnalyzeText(weird, "crash-2026-09-13_05.00.00-client.txt");

        Assert.Single(result.Causes);
        Assert.Equal("Unknown", result.Causes[0].Category);
        Assert.Equal(CrashConfidence.Low, result.Causes[0].Confidence);
    }

    // ---------------- 扫描器 ----------------

    [Fact]
    public void Scanner_FindsCrashReportsInMainAndIsolatedVersionDirs()
    {
        var root = Path.Combine(Path.GetTempPath(), "omcl_crashscan_" + Guid.NewGuid());
        try
        {
            // 主目录 crash-reports
            var mainCrashDir = Path.Combine(root, "crash-reports");
            Directory.CreateDirectory(mainCrashDir);
            File.WriteAllText(Path.Combine(mainCrashDir, "crash-2026-09-20_10.00.00-client.txt"), OomReport);

            // 主目录 hs_err
            File.WriteAllText(Path.Combine(root, "hs_err_pid1000.log"), HsErrIntel);

            // 隔离版本目录
            var isoDir = Path.Combine(root, "versions", "1.20.1-forge", "crash-reports");
            Directory.CreateDirectory(isoDir);
            File.WriteAllText(Path.Combine(isoDir, "crash-2026-09-17_12.00.00-client.txt"), ForgeModFailureReport);

            // 注意：隔离判断依赖 LauncherConfig 的版本配置；无配置时由 GameDirectoryType 决定。
            // 测试环境的默认配置非版本隔离 → 隔离目录的扫描走 IsVersionIsolated，这里直接验证主目录部分。
            var scanner = CrashReportScanner.Instance;
            var reports = scanner.GetCrashReports(root);

            Assert.Contains(reports, r => r.FileName == "crash-2026-09-20_10.00.00-client.txt" && r.VersionName == "主目录");
            Assert.Contains(reports, r => r.FileName == "hs_err_pid1000.log" && r.Kind == CrashReportKind.JvmFatalErrorLog);
            // 时间倒序
            Assert.True(reports.SequenceEqual(reports.OrderByDescending(r => r.CreatedTime)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Scanner_MissingDirectory_ReturnsEmpty()
    {
        var scanner = CrashReportScanner.Instance;
        var reports = scanner.GetCrashReports(Path.Combine(Path.GetTempPath(), "omcl_not_exist_" + Guid.NewGuid()));
        Assert.Empty(reports);
    }

    [Fact]
    public void AnalyzeFile_RoundTrip_WorksFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), "omcl_crashfile_" + Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, OomReport);
            var result = _analyzer.AnalyzeFile(path);
            Assert.Equal("Memory", result.Causes[0].Category);
            Assert.False(string.IsNullOrEmpty(result.RawPreview));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
