using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Models;

public enum NotificationPosition
{
    Center,
    BottomRight
}

/// <summary>
/// Velopack自动更新通道
/// </summary>
public enum UpdateChannel
{
    /// <summary>正式版</summary>
    Stable,
    /// <summary>测试版</summary>
    Beta,
    /// <summary>预发布版</summary>
    RC,
    /// <summary>预览版</summary>
    Preview
}

public class LauncherConfig
{
    public string GetActualJavaPath(string? minecraftVersion = null)
    {
        switch (JavaSelectionMode)
        {
            case 0:
                if (!string.IsNullOrEmpty(minecraftVersion))
                {
                    var autoPath = JavaDetector.SelectJavaForMinecraftVersion(minecraftVersion);
                    if (!string.IsNullOrEmpty(autoPath))
                    {
                        return autoPath;
                    }
                }

                var bestJava = JavaDetector.SelectBestJava();
                return bestJava?.Path ?? GetDefaultJavaPath();

            case 1:
                return string.IsNullOrEmpty(JavaPath) ? GetDefaultJavaPath() : JavaPath;

            case 2:
                return string.IsNullOrEmpty(CustomJavaPath) ? JavaPath : CustomJavaPath;

            default:
                return JavaPath;
        }
    }

    private static string GetDefaultJavaPath()
    {
        if (OperatingSystem.IsWindows()) return "javaw.exe";
        return "java";
    }

    public string GetRunDirectory(string versionName)
    {
        return IsVersionIsolated(versionName)
            ? Path.Combine(GameDirectory, "versions", versionName)
            : GameDirectory;
    }

    /// <summary>
    /// 判断指定版本是否启用版本隔离（优先读版本级配置，缺省回落到全局游戏目录类型）。
    /// 截图、崩溃报告、存档等按运行目录归位的功能共用此判断。
    /// </summary>
    public bool IsVersionIsolated(string versionName)
    {
        var versionPath = Path.Combine(GameDirectory, "versions", versionName);
        var versionIsolation = VersionConfigService.GetVersionIsolation(versionPath);

        if (versionIsolation.HasValue)
        {
            return versionIsolation.Value;
        }

        return GameDirectoryType == GameDirectoryType.VersionFolder;
    }

    public string GetModsDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "mods");

    public string GetResourcePacksDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "resourcepacks");

    public string GetShaderPacksDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "shaderpacks");

    public string GetSavesDirectory(string versionName) => Path.Combine(GetRunDirectory(versionName), "saves");

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DownloadSource DownloadSource { get; set; } = DownloadSource.BMCLAPI;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MirrorSourceMode MirrorSourceMode { get; set; } = MirrorSourceMode.PreferMirror;

    public int MaxMemory { get; set; } = 4096;

    public int MinMemory { get; set; } = 1024;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DirectoryLocation GameDirectoryLocation { get; set; } = DirectoryLocation.AppData;

    public string CustomGameDirectory { get; set; } = "";

    [JsonIgnore]
    public string GameDirectory
        => GameDirectoryLocation switch
        {
            DirectoryLocation.AppData => GetDefaultAppdataGameDirectory(),
            DirectoryLocation.RunningDirectory => Path.Combine(
                VersionInfo.GetAppBaseDirectory(),
                ".minecraft"),
            DirectoryLocation.Custom => string.IsNullOrEmpty(CustomGameDirectory)
                ? GetDefaultAppdataGameDirectory()
                : CustomGameDirectory,
            _ => GetDefaultAppdataGameDirectory()
        };

    public static string GetDefaultAppdataGameDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "minecraft");
        }
        if (OperatingSystem.IsLinux())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".minecraft");
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");
    }

    public int JavaSelectionMode { get; set; } = 0;

    public string JavaPath { get; set; } = OperatingSystem.IsWindows() ? "javaw.exe" : "java";

    public string CustomJavaPath { get; set; } = "";

    public string JvmArguments { get; set; } = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions";

    public int MaxDownloadThreads { get; set; } = 8;

    public bool DownloadAssetsWithGame { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameDirectoryType GameDirectoryType { get; set; } = GameDirectoryType.RootFolder;

    public bool CloseAfterLaunch { get; set; } = false;

    public bool ShowGameLogOnLaunch { get; set; } = false;

    public bool AutoCheckUpdate { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    public bool SkipSslValidation { get; set; } = false;

    public bool EnableFileHashVerification { get; set; } = true;

    public int ThemeMode { get; set; } = 0;

    /// <summary>
    /// 自定义强调色（hex 字符串），默认绿色 #10B981，为空时回退默认
    /// </summary>
    public string AccentColor { get; set; } = "#10B981";

    /// <summary>
    /// 圆角半径（0-28），0 为直角，默认 12
    /// </summary>
    public int CornerRadius { get; set; } = 12;

    /// <summary>
    /// 密度：0=紧凑 1=标准 2=宽松
    /// </summary>
    public int Density { get; set; } = 1;

    /// <summary>
    /// 动画级别：0=禁用 1=标准 2=华丽
    /// </summary>
    public int AnimationLevel { get; set; } = 1;

    /// <summary>是否启用主内容区背景壁纸（只表示开关意愿，是否真正生效见 <see cref="IsWallpaperActive"/>）</summary>
    public bool WallpaperEnabled { get; set; } = false;

    /// <summary>
    /// 壁纸条目列表（顺序即显示 / 轮播顺序；空表示无壁纸）。
    /// 取代原 <c>WallpaperPath</c> 单路径字段。
    /// </summary>
    public List<WallpaperItem> WallpaperItems { get; set; } = [];

    /// <summary>主内容壁纸透明度（0-1）</summary>
    public double WallpaperOpacity { get; set; } = 0.35;

    /// <summary>壁纸显示方式：0=Fill 1=Uniform 2=UniformToFill 3=None(原尺寸)</summary>
    public int WallpaperStretch { get; set; } = 1;

    /// <summary>是否将壁纸扩展到导航栏（通过降低导航栏透明度实现）</summary>
    public bool WallpaperExtendToNav { get; set; } = false;

    /// <summary>导航栏背景透明度（0-1，越小越透出壁纸），仅扩展时生效</summary>
    public double NavBackgroundOpacity { get; set; } = 0.7;

    /// <summary>是否播放动图动画（false 时只显示首帧）</summary>
    public bool WallpaperPlayAnimated { get; set; } = true;

    /// <summary>动图帧率上限（1-60，默认 24）</summary>
    public int WallpaperMaxFps { get; set; } = 24;

    /// <summary>
    /// 解码长边上限像素（0=不限制，默认 1920）。
    /// **只能向下夹紧**：实际解码尺寸 = min(源长边, 窗口物理长边, 本值)。
    /// 实测 codec 不支持升采样（返回 InvalidScale），因此对小图设更大的值不会放大解码结果。
    /// </summary>
    public int WallpaperMaxDecodeEdge { get; set; } = 1920;

    /// <summary>轮播间隔秒数（0=不轮播，默认 0）。支持任意秒数，UI 另提供预设档位</summary>
    public int WallpaperSlideIntervalSeconds { get; set; } = 0;

    /// <summary>轮播顺序：0=顺序 1=随机 2=每次启动随机起点</summary>
    public int WallpaperSlideMode { get; set; } = 0;

    /// <summary>轮播过渡时长毫秒（0-2000，默认 600）</summary>
    public int WallpaperTransitionMs { get; set; } = 600;

    /// <summary>窗口失焦时暂停动图</summary>
    public bool WallpaperPauseOnUnfocused { get; set; } = false;

    /// <summary>电池供电时暂停动图</summary>
    public bool WallpaperPauseOnBattery { get; set; } = true;

    /// <summary>
    /// 壁纸是否实际生效：开关打开且列表非空。
    /// 这样"列表被清空但开关仍开着"不会产生歧义——UI 显示空态，渲染层不加载任何内容。
    /// </summary>
    [JsonIgnore]
    public bool IsWallpaperActive => WallpaperEnabled && WallpaperItems.Count > 0;

    /// <summary>自定义字体名称，空表示使用系统默认字体</summary>
    public string? CustomFontFamily { get; set; }

    /// <summary>自定义字重（100-950，400=Normal）</summary>
    public int CustomFontWeight { get; set; } = 400;

    public string? SelectedVersion { get; set; }

    public string? SelectedAccountId { get; set; }

    public bool IsNavCollapsed { get; set; } = false;

    /// <summary>
    /// 是否已完成欢迎界面流程（false 时启动显示欢迎窗口）
    /// </summary>
    public bool WelcomeCompleted { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationPosition NotificationPosition { get; set; } = NotificationPosition.Center;

    public int NotificationAutoCloseSeconds { get; set; } = 5;

    /// <summary>
    /// 是否使用操作系统原生标题栏。默认 false：所有窗口统一走启动器自定义标题栏。
    /// </summary>
    public bool UseSystemTitleBar { get; set; } = false;

    public List<ServerInfo> Servers { get; set; } = [];

    public List<HomeCardConfig> HomeCards { get; set; } = [];

    /// <summary>主页布局（行 + 组件）。null 时首次使用会从旧版 HomeCards 配置迁移生成</summary>
    public HomeLayoutConfig? HomeLayout { get; set; }

    /// <summary>
    /// 获取主页布局；不存在时从旧版 HomeCards（IsEnabled/Order）推导默认布局。
    /// 注意：只填充内存字段，不自动写盘，由调用方决定何时 Save。
    /// </summary>
    public HomeLayoutConfig GetHomeLayout()
    {
        if (HomeLayout == null)
        {
            HomeLayout = HomeLayoutConfig.CreateDefault(HomeCards);
        }
        else
        {
            // 操作区去组件化后，旧版布局里的操作区组件改成读出时清理
            HomeLayout.RemoveLegacyActionComponents();
        }
        return HomeLayout;
    }

    public List<VersionIsolationSetting> VersionIsolationSettings { get; set; } = [];

    public List<string> CustomGameDirectories { get; set; } = [];

    /// <summary>
    /// 自定义版本分组列表（系统分组不存储在此，由 VersionGroup.GetSystemGroups() 提供）
    /// </summary>
    public List<VersionGroup> CustomVersionGroups { get; set; } = [];

    /// <summary>
    /// 版本到分组的映射：versionId -> groupId
    /// </summary>
    public Dictionary<string, string> VersionGroupMappings { get; set; } = new();

    public static string GetConfigFilePath()
    {
        return Path.Combine(
            VersionInfo.GetAppBaseDirectory(),
            "OMCL",
            "config",
            "config.json");
    }

    private static string _currentConfigPath = GetConfigFilePath();

    public void Save()
    {
        var configPath = GetConfigFilePath();

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(configPath, json);

        _currentConfigPath = configPath;
        FileHashVerifier.InvalidateCache();
    }

    public static LauncherConfig Load()
    {
        var configPath = GetConfigFilePath();

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                if (config != null)
                {
                    _currentConfigPath = configPath;
                    return config;
                }
            }
            catch
            {
            }
        }

        var defaultConfig = new LauncherConfig();
        _currentConfigPath = configPath;
        return defaultConfig;
    }

    public string GetAccountFilePath()
    {
        return Path.Combine(
            VersionInfo.GetAppBaseDirectory(),
            "OMCL",
            "config",
            "accounts.json");
    }

    public string GetPluginDirectory()
    {
        return Path.Combine(
            VersionInfo.GetAppBaseDirectory(),
            "OMCL",
            "plugins");
    }

    public string GetDataDirectory()
    {
        return Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL");
    }
}
