# ObsMCLauncher 插件开发指南

欢迎来到 ObsMCLauncher 插件开发！本文档将指导您如何开发、测试和发布插件。

---

## 📖 目录

- [插件系统介绍](#插件系统介绍)
- [开发环境要求](#开发环境要求)
- [插件目录位置](#插件目录位置)
- [插件结构](#插件结构)
- [插件接口](#插件接口)
- [API 参考](#api-参考)
  - [事件系统](#1-事件系统)
  - [注册UI标签页](#2-注册ui标签页)
  - [注册主页卡片](#3-注册主页卡片)
  - [插件数据目录](#4-插件数据目录)
  - [启动器版本信息](#5-启动器版本信息)
  - [通知系统](#6-通知系统)
  - [自定义命令](#7-自定义命令)
  - [日志写入](#8-日志写入)
  - [获取已安装版本列表](#9-获取已安装版本列表)
  - [获取当前账户信息](#10-获取当前账户信息)
  - [游戏启动生命周期钩子](#11-游戏启动生命周期钩子)
  - [提交下载请求](#12-提交下载请求)
- [开发流程](#开发流程)
- [测试插件](#测试插件)
- [用户安装插件](#用户安装插件)
- [发布流程](#发布流程)
- [示例插件](#示例插件)
- [常见问题](#常见问题)

---

## 🔌 插件系统介绍

ObsMCLauncher 采用基于 .NET Assembly 的插件系统，支持开发者使用 C# 编写插件来扩展启动器功能。

### 插件特性

- ✅ 原生性能，无沙箱限制
- ✅ 完整访问启动器 API
- ✅ 支持 UI 扩展（基于 Avalonia）
- ✅ 事件驱动架构
- ✅ 配置文件支持
- ✅ 跨平台支持（Windows/macOS/Linux）

### 插件类型

- **功能增强插件**：添加新功能（如皮肤管理、服务器监控）
- **UI 插件**：扩展用户界面
- **工具插件**：提供实用工具（如备份、性能监控）
- **集成插件**：与第三方服务集成

---

## 🛠️ 开发环境要求

### 必需工具

- **.NET 8.0 SDK** 或更高版本
- **Visual Studio 2022** 或 **Rider** 或 **VS Code**
- **Git**（用于版本控制）

### 推荐安装

- **Avalonia VS Code Extension**（用于XAML预览）
- **ILSpy** 或 **dnSpy**（用于反编译和调试）

---

## 📁 插件目录位置

### 插件安装目录

ObsMCLauncher 的插件目录是**固定的**，位于启动器运行目录下的 `OMCL\plugins\` 文件夹：

```
运行目录\OMCL\plugins\
```

**说明**：
- 插件目录位置是固定的，不支持自定义
- 所有插件都安装在同一个 `OMCL\plugins\` 目录下
- 每个插件有自己独立的子文件夹（文件夹名必须与插件ID一致）
- 这种设计便于便携使用和备份

### 目录结构

```
ObsMCLauncher/                           # 启动器运行目录
├── ObsMCLauncher.Desktop.dll
├── OMCL/                                # 启动器数据目录
│   ├── config/                          # 配置文件
│   │   ├── config.json
│   │   └── accounts.json
│   └── plugins/                         # 插件目录（默认位置）
│       ├── example-hello-plugin/        # 插件文件夹
│       │   ├── example-hello-plugin.dll # 插件程序集
│       │   ├── plugin.json              # 插件元数据
│       │   ├── icon.png                 # 插件图标（可选）
│       │   ├── config.json              # 插件配置（可选，由开发者自定义）
│       │   └── data/                    # 插件数据（可选，由开发者自定义）
│       └── another-plugin/              # 另一个插件
│           ├── another-plugin.dll
│           ├── plugin.json
│           ├── icon.png
│           └── settings.json            # 插件自定义的配置文件
└── (其他启动器文件)
```

### 插件命名规范

- **插件文件夹名**：必须与插件 ID 完全一致
- **DLL 文件名**：建议与插件 ID 一致（如 `my-plugin.dll`）
- **插件 ID 规则**：
  - 只能包含小写字母、数字和连字符 `-`
  - 必须以字母开头
  - 长度 3-50 个字符
  - 示例：`hello-plugin`, `skin-manager`, `backup-tool`

---

## 📦 插件结构

### 目录结构

```
YourPlugin/
├── YourPlugin.csproj          # 项目文件
├── Plugin.cs                  # 插件主类（实现 ILauncherPlugin）
├── plugin.json                # 插件元数据
├── icon.png                   # 插件图标（可选，128x128）
├── README.md                  # 插件说明（必需）
└── LICENSE                    # 开源协议
```

### plugin.json 格式

```json
{
  "id": "your-plugin-id",
  "name": "您的插件名称",
  "version": "1.0.0",
  "author": "您的名字",
  "description": "插件简要描述",
  "repository": "https://github.com/yourusername/your-plugin",
  "minLauncherVersion": "1.0.0",
  "dependencies": [],
  "tags": ["Windows", "工具"],
  "category": "utility"
}
```

### 字段说明

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `id` | string | ✅ | 插件唯一标识符（小写字母、数字、连字符） |
| `name` | string | ✅ | 插件显示名称 |
| `version` | string | ✅ | 版本号（遵循 SemVer） |
| `author` | string | ✅ | 作者名称 |
| `description` | string | ✅ | 简短描述（不超过 200 字） |
| `repository` | string | ⭕ | 源代码仓库 URL |
| `minLauncherVersion` | string | ⭕ | 最低启动器版本要求（默认 1.0.0） |
| `dependencies` | array | ⭕ | 依赖的其他插件ID列表 |
| `tags` | array | ⭕ | 标签列表，支持平台标签：`Windows`、`Linux`、`macOS` |
| `category` | string | ⭕ | 分类ID |
| `homepage` | string | ⭕ | 插件主页 URL |
| `license` | string | ⭕ | 开源协议 |
| `icon` | string | ⭕ | 图标文件名（默认 icon.png） |

---

## 🔧 插件接口

### ILauncherPlugin 接口

所有插件必须实现 `ILauncherPlugin` 接口：

```csharp
namespace ObsMCLauncher.Core.Plugins
{
    public interface ILauncherPlugin
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }
        
        void OnLoad(IPluginContext context);
        void OnUnload();
        void OnShutdown();
    }
}
```

### IPluginContext 接口

通过插件上下文访问启动器功能：

```csharp
namespace ObsMCLauncher.Core.Plugins
{
    public interface IPluginContext
    {
        string LauncherVersion { get; }
        string PluginDataDirectory { get; }

        void RegisterTab(string title, string tabId, string? icon = null, object? payload = null);
        void SubscribeEvent(string eventName, Action<object?> handler);
        void PublishEvent(string eventName, object? eventData);

        void RegisterHomeCard(
            string cardId,
            string title,
            string description,
            string? icon = null,
            string? commandId = null,
            object? payload = null);

        void UnregisterHomeCard(string cardId);

        void ShowNotification(string title, string message, string type = "info", int? durationSeconds = null);
        void UpdateNotification(string notificationId, string message, double? progress = null);
        void CloseNotification(string notificationId);

        void RegisterCommand(string commandId, Action<object?> handler);
        void UnregisterCommand(string commandId);

        // ===== 扩展 API=====

        /// <summary>写入启动器统一日志</summary>
        void LogMessage(PluginLogLevel level, string message);

        /// <summary>获取已安装版本只读列表；无任何版本或异常时返回空列表</summary>
        IReadOnlyList<PluginVersionInfo> GetInstalledVersions();

        /// <summary>获取当前默认/选中的账户精简信息（不含任何令牌）；未选中时返回 null</summary>
        PluginAccountInfo? GetCurrentAccount();

        /// <summary>注册游戏启动生命周期钩子</summary>
        void RegisterGameLaunchHook(string hookId, GameLaunchPhase phase, Action<GameLaunchHookContext> handler);

        /// <summary>注销启动生命周期钩子</summary>
        void UnregisterGameLaunchHook(string hookId);

        /// <summary>提交下载请求给启动器下载管理器；返回任务ID，被拒绝时返回空字符串</summary>
        string RequestDownload(PluginDownloadRequest request);

        /// <summary>
        /// 全局事件名称常量
        /// </summary>
        public static class EventNames
        {
            public const string GameLaunched = "GameLaunched";
            public const string GameClosed = "GameClosed";
            public const string VersionDownloaded = "VersionDownloaded";
            public const string VersionInstalling = "VersionInstalling";
            public const string VersionInstalled = "VersionInstalled";
            public const string AccountChanged = "AccountChanged";
            public const string DownloadProgress = "DownloadProgress";
        }
    }
}
```

---

## 🔧 API 参考

### 1. 事件系统

订阅和发布事件。建议使用 `IPluginContext.EventNames` 常量避免拼写错误：

```csharp
using ObsMCLauncher.Core.Plugins;
using ObsMCLauncher.Core.Plugins.Events;

public void OnLoad(IPluginContext context)
{
    context.SubscribeEvent(IPluginContext.EventNames.GameLaunched, OnGameLaunched);
    context.SubscribeEvent(IPluginContext.EventNames.VersionInstalled, OnVersionInstalled);
    context.SubscribeEvent(IPluginContext.EventNames.AccountChanged, OnAccountChanged);
    context.SubscribeEvent(IPluginContext.EventNames.DownloadProgress, OnDownloadProgress);
}

private void OnGameLaunched(object? eventData)
{
    System.Diagnostics.Debug.WriteLine("游戏已启动");
}

private void OnVersionInstalled(object? eventData)
{
    if (eventData is VersionInstalledEventArgs args)
    {
        System.Diagnostics.Debug.WriteLine(
            args.Success
                ? $"版本 {args.VersionName} 安装成功，目录：{args.VersionDirectory}"
                : $"版本 {args.VersionName} 安装失败：{args.ErrorMessage}");
    }
}

private void OnAccountChanged(object? eventData)
{
    if (eventData is AccountChangedEventArgs args)
    {
        System.Diagnostics.Debug.WriteLine($"账户变更：{args.ChangeType} - {args.Username}");
    }
}

private void OnDownloadProgress(object? eventData)
{
    if (eventData is DownloadProgressEventArgs args)
    {
        System.Diagnostics.Debug.WriteLine($"下载进度：{args.TaskName} - {args.Progress:F1}%");
    }
}
```

**可用事件**：

| 事件名 | 常量 | 说明 | 事件数据类型 |
|--------|------|------|-------------|
| `GameLaunched` | `EventNames.GameLaunched` | 游戏启动 | - |
| `GameClosed` | `EventNames.GameClosed` | 游戏关闭 | - |
| `VersionDownloaded` | `EventNames.VersionDownloaded` | 版本下载完成 | - |
| `VersionInstalling` | `EventNames.VersionInstalling` | 版本安装开始 | `VersionInstallingEventArgs` |
| `VersionInstalled` | `EventNames.VersionInstalled` | 版本安装完成/失败 | `VersionInstalledEventArgs` |
| `AccountChanged` | `EventNames.AccountChanged` | 账户变更 | `AccountChangedEventArgs` |
| `DownloadProgress` | `EventNames.DownloadProgress` | 下载进度更新 | `DownloadProgressEventArgs` |

**VersionInstallingEventArgs** 属性：
- `McVersion` - Minecraft 版本号
- `VersionName` - 自定义版本名称
- `LoaderType` - 加载器类型（vanilla, forge, fabric, quilt, neoforge, optifine）
- `LoaderVersion` - 加载器版本
- `GameDirectory` - 游戏目录路径

**VersionInstalledEventArgs** 属性：
- 继承 `VersionInstallingEventArgs` 的所有属性
- `VersionDirectory` - 版本安装目录（完整路径）
- `Success` - 是否安装成功
- `ErrorMessage` - 失败时的错误信息

**AccountChangedEventArgs** 属性：
- `ChangeType` - 变更类型（`Switched` 切换默认、`Added` 添加、`Removed` 删除、`Updated` 更新）
- `AccountId` - 账户ID
- `Username` - 用户名
- `AccountType` - 账户类型（Offline, Microsoft, Yggdrasil）

**DownloadProgressEventArgs** 属性：
- `TaskId` - 下载任务ID
- `TaskName` - 任务名称
- `TaskType` - 任务类型（Version, Assets, Mod, Resource）
- `Progress` - 当前进度（0-100）
- `StatusMessage` - 状态消息
- `DownloadSpeed` - 下载速度（字节/秒）
- `Status` - 下载状态（`Downloading`, `Completed`, `Failed`, `Cancelled`）

插件也可以发布自定义事件。

### 2. 注册UI标签页

插件可以在"更多"页面添加自己的标签页：

```csharp
// 注册普通标签页（显示默认文本信息）
public void OnLoad(IPluginContext context)
{
    context.RegisterTab(
        "我的插件",           // 标签页标题
        "my-plugin-tab",     // 标签页ID（唯一）
        "Star",              // 图标名称（可选）
        null                 // 自定义数据（可选）
    );
}
```

**注册带自定义UI的标签页**：

```csharp
using Avalonia.Controls;

public void OnLoad(IPluginContext context)
{
    // 创建自定义UI控件
    var panel = new StackPanel();
    panel.Children.Add(new TextBlock { Text = "Hello from plugin!" });
    panel.Children.Add(new Button { Content = "Click Me" });

    // 注册带自定义UI的标签页
    context.RegisterTab(
        "我的插件",           // 标签页标题
        "my-plugin-tab",     // 标签页ID（唯一）
        panel,               // 自定义UI控件（Avalonia Control）
        "Star",              // 图标名称（可选）
        null                 // 自定义数据（可选）
    );
}
```

**说明**：
- 标签页会显示在"更多"页面的顶部导航栏
- `tabId` 必须唯一，建议使用插件ID作为前缀
- 图标使用 Material Design 图标名称
- 传入 `Control` 对象时，标签页直接渲染该控件（方案A）
- 不传 `Control` 时，标签页显示默认文本信息

### 3. 注册主页卡片

插件可以在主页添加自定义卡片：

```csharp
public void OnLoad(IPluginContext context)
{
    context.RegisterHomeCard(
        "my-card",                    // 卡片ID（在插件内唯一）
        "我的插件卡片",                // 卡片标题
        "这是一个示例卡片",            // 卡片描述
        "🌟",                          // 图标（可选，emoji或文本）
        "url:https://example.com",     // 命令ID（可选）
        null                           // 自定义数据（可选）
    );
}

public void OnUnload()
{
    _context?.UnregisterHomeCard("my-card");
}
```

**命令ID支持的格式**：
- `url:https://example.com` - 打开外部网页链接
- `navigate:multiplayer` - 跳转到启动器内部页面（支持的页面：`multiplayer`、`resources`、`accounts`、`versions`、`settings`、`more`）
- `command:{pluginId}.{commandId}` - 执行插件注册的自定义命令
- 留空或null - 卡片不可点击（仅展示信息）

**示例**：
```csharp
// 打开外部链接
context.RegisterHomeCard(
    "wiki-card",
    "查看Wiki",
    "访问Minecraft Wiki",
    "📖",
    "url:https://zh.minecraft.wiki"
);

// 跳转到内部页面
context.RegisterHomeCard(
    "mods-card",
    "下载Mod",
    "浏览和下载Mod资源",
    "📦",
    "navigate:resources"
);

// 执行自定义命令
context.RegisterCommand("open-backup", OnOpenBackup);
context.RegisterHomeCard(
    "backup-card",
    "备份数据",
    "一键备份游戏存档",
    "💾",
    "command:backup-plugin.open-backup"
);

private void OnOpenBackup(object? payload)
{
    // 执行备份逻辑
    string dataDir = _context.PluginDataDirectory;
    // ...
}
```

### 4. 插件数据目录

```csharp
public void OnLoad(IPluginContext context)
{
    string dataDir = context.PluginDataDirectory;
    
    var configPath = Path.Combine(dataDir, "config.json");
    File.WriteAllText(configPath, "{}");
    
    var dataFolder = Path.Combine(dataDir, "data");
    Directory.CreateDirectory(dataFolder);
}
```

### 5. 启动器版本信息

```csharp
public void OnLoad(IPluginContext context)
{
    string version = context.LauncherVersion;
    
    if (new Version(version) < new Version("1.1.0"))
    {
        System.Diagnostics.Debug.WriteLine("启动器版本过低");
    }
}
```

### 6. 通知系统

插件可以显示、更新和关闭通知：

```csharp
public void OnLoad(IPluginContext context)
{
    // 显示简单通知（默认3秒后自动关闭）
    context.ShowNotification("提示", "操作成功", "success");
    
    // 显示错误通知（5秒后关闭）
    context.ShowNotification("错误", "操作失败", "error", 5);
    
    // 显示进度通知（无限持续时间）
    var notifId = context.ShowNotification("下载中", "正在下载...", "progress", null);
    
    // 更新通知
    context.UpdateNotification(notifId, "下载中 50%", 50);
    
    // 关闭通知
    context.CloseNotification(notifId);
}
```

**通知类型**：
- `info` - 信息通知（蓝色）
- `success` - 成功通知（绿色）
- `warning` - 警告通知（黄色）
- `error` - 错误通知（红色）
- `progress` - 进度通知（带进度条）

**持续时间**：
- 不传或传 `null`：默认3秒自动关闭
- 传具体秒数：指定秒数后关闭
- 传 `0` 或负数：无限持续时间，需手动关闭

### 7. 自定义命令

插件可以注册自定义命令，供主页卡片或其他交互触发：

```csharp
public void OnLoad(IPluginContext context)
{
    // 注册命令
    context.RegisterCommand("open-backup", OnOpenBackup);
    context.RegisterCommand("check-update", OnCheckUpdate);

    // 在卡片中使用 command:{pluginId}.{commandId} 格式引用
    context.RegisterHomeCard(
        "backup-card",
        "备份数据",
        "一键备份游戏存档",
        "💾",
        "command:backup-plugin.open-backup"
    );
}

private void OnOpenBackup(object? payload)
{
    // payload 为卡片注册时的 Payload 参数
    _context?.ShowNotification("备份", "正在备份存档...", "info", 0);
}

private void OnCheckUpdate(object? payload)
{
    // 检查更新逻辑
}

public void OnUnload()
{
    // 命令会在插件卸载时自动清理，无需手动注销
    _context?.UnregisterHomeCard("backup-card");
}
```

**说明**：
- 命令ID在插件内唯一，系统会自动拼接为 `{pluginId}.{commandId}`
- 插件卸载/禁用时，所有命令自动清理
- 卡片使用 `command:{pluginId}.{commandId}` 格式引用命令
- `payload` 参数来自卡片的 `Payload` 属性

### 8. 日志写入

将插件日志写入启动器统一日志系统，便于排查插件问题。日志与启动器自身日志同源，可在启动器的"开发控制台"或日志文件中查看。

**方法签名**：

```csharp
void LogMessage(PluginLogLevel level, string message);
```

**参数**：

| 参数 | 类型 | 说明 |
|------|------|------|
| `level` | `PluginLogLevel` | 日志级别：`Debug` / `Info` / `Warning` / `Error` |
| `message` | `string` | 日志消息内容（空字符串或 null 时不写入） |

**返回值**：无

**异常处理**：回调内部异常不会抛出到调用方，由启动器统一捕获记录。

**示例**：

```csharp
public void OnLoad(IPluginContext context)
{
    context.LogMessage(PluginLogLevel.Info, "插件已加载");

    try
    {
        // 业务逻辑
    }
    catch (Exception ex)
    {
        context.LogMessage(PluginLogLevel.Error, $"初始化失败: {ex.Message}");
    }
}
```

### 9. 获取已安装版本列表

获取启动器中已安装的 Minecraft 版本只读列表，用于插件展示版本信息、按版本执行操作（如备份、迁移、统计）。

**方法签名**：

```csharp
IReadOnlyList<PluginVersionInfo> GetInstalledVersions();
```

**返回值**：`IReadOnlyList<PluginVersionInfo>` - 已安装版本只读列表

**PluginVersionInfo 字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `VersionId` | `string` | 版本ID（如 `1.20.1-Forge`） |
| `McVersion` | `string` | Minecraft 原版版本号（如 `1.20.1`） |
| `LoaderType` | `string` | 加载器类型：`vanilla` / `forge` / `fabric` / `quilt` / `neoforge` / `optifine` |
| `VersionDirectory` | `string` | 版本目录绝对路径 |
| `LastPlayed` | `DateTime?` | 最后游玩时间；从未游玩为 null |

**安全说明**：仅返回版本元数据，不包含任何账户、令牌、JVM 参数等敏感字段。回调异常或未注入时返回空列表。

**示例**：

```csharp
public void OnLoad(IPluginContext context)
{
    var versions = context.GetInstalledVersions();
    context.LogMessage(PluginLogLevel.Info, $"共 {versions.Count} 个已安装版本");

    foreach (var v in versions)
    {
        context.LogMessage(PluginLogLevel.Debug,
            $"{v.VersionId} ({v.LoaderType}) - {v.McVersion}");
    }

    // 仅备份 Forge 版本
    var forgeVersions = versions.Where(v => v.LoaderType == "forge").ToList();
    foreach (var v in forgeVersions)
    {
        BackupVersion(v.VersionDirectory);
    }
}
```

### 10. 获取当前账户信息

获取当前默认/选中的账户精简信息，用于插件显示用户名、UUID，或按账户执行操作。

**方法签名**：

```csharp
PluginAccountInfo? GetCurrentAccount();
```

**返回值**：`PluginAccountInfo?` - 账户精简信息；未选中账户或异常时返回 `null`

**PluginAccountInfo 字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `AccountId` | `string` | 账户内部ID |
| `Username` | `string` | 用户名（离线模式为玩家自定义名，微软模式为 Xbox GT） |
| `AccountType` | `string` | 账户类型：`Offline` / `Microsoft` / `Yggdrasil` |
| `UUID` | `string` | Minecraft UUID（不带连字符的 32 位十六进制） |
| `IsDefault` | `bool` | 是否为默认账户 |

**安全说明**：**不返回任何令牌字段**（不含 Access Token / Xbox Live Token / Minecraft Services Token）。如需发起微软 API 请求，请让用户自行授权。

**示例**：

```csharp
public void OnLoad(IPluginContext context)
{
    var account = context.GetCurrentAccount();
    if (account == null)
    {
        context.LogMessage(PluginLogLevel.Warning, "未选中账户");
        return;
    }

    context.LogMessage(PluginLogLevel.Info,
        $"当前账户: {account.Username} ({account.AccountType})");
    context.LogMessage(PluginLogLevel.Debug, $"UUID: {account.UUID}");
}
```

### 11. 游戏启动生命周期钩子

注册游戏启动生命周期钩子，在启动前/启动后/退出/崩溃时执行自定义逻辑。可在启动前修改 JVM/游戏参数，或拦截启动。

**方法签名**：

```csharp
void RegisterGameLaunchHook(string hookId, GameLaunchPhase phase, Action<GameLaunchHookContext> handler);
void UnregisterGameLaunchHook(string hookId);
```

**参数**：

| 参数 | 类型 | 说明 |
|------|------|------|
| `hookId` | `string` | 钩子唯一标识（在插件内唯一） |
| `phase` | `GameLaunchPhase` | 触发阶段（见下表） |
| `handler` | `Action<GameLaunchHookContext>` | 回调函数，接收钩子上下文 |

**GameLaunchPhase 枚举**：

| 阶段 | 说明 | 可修改字段 |
|------|------|-----------|
| `BeforeLaunch` | 启动前（可拦截启动） | `CancelLaunch` / `ExtraJvmArguments` / `ExtraGameArguments` |
| `AfterLaunch` | 游戏进程已启动 | - |
| `OnExited` | 游戏进程退出 | `ExitCode` |
| `OnCrash` | 检测到崩溃 | `ExitCode` / `CrashReport` |

**GameLaunchHookContext 字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `VersionId` | `string` | 启动的版本ID |
| `McVersion` | `string` | Minecraft 版本号 |
| `GameDirectory` | `string` | 游戏运行目录 |
| `JavaPath` | `string` | Java 可执行文件路径 |
| `ExitCode` | `int` | 进程退出码（仅 `OnExited` / `OnCrash` 有效；正常退出为 0） |
| `CrashReport` | `string?` | 崩溃报告内容（仅 `OnCrash` 有效） |
| `CancelLaunch` | `bool` | 设为 true 中止启动（仅 `BeforeLaunch` 有效） |
| `ExtraJvmArguments` | `List<string>` | 追加 JVM 参数（仅 `BeforeLaunch` 有效） |
| `ExtraGameArguments` | `List<string>` | 追加游戏参数（仅 `BeforeLaunch` 有效） |

**触发顺序**：同一阶段有多个钩子时，按 `{pluginId}.{hookId}` 字典序触发。`BeforeLaunch` 阶段被某个钩子设为 `CancelLaunch = true` 后，后续 `BeforeLaunch` 钩子不再调用。

**异常处理**：单个钩子抛出异常不会影响其他钩子执行，异常由启动器统一记录。

**示例**：

```csharp
public void OnLoad(IPluginContext context)
{
    // 启动前追加 JVM 参数
    context.RegisterGameLaunchHook("add-jvm-args",
        GameLaunchPhase.BeforeLaunch, OnBeforeLaunch);

    // 启动后记录日志
    context.RegisterGameLaunchHook("log-launched",
        GameLaunchPhase.AfterLaunch, OnAfterLaunch);

    // 崩溃时上传报告
    context.RegisterGameLaunchHook("upload-crash",
        GameLaunchPhase.OnCrash, OnCrash);
}

private void OnBeforeLaunch(GameLaunchHookContext ctx)
{
    // 为低版本 Minecraft 强制使用 Java 8（演示用，实际应通过 JavaDetector）
    if (ctx.McVersion.StartsWith("1.12."))
    {
        ctx.ExtraJvmArguments.Add("-Djava.util.Arrays.useLegacyMergeSort=true");
    }

    // 启用插件调试模式时追加调试参数
    ctx.ExtraJvmArguments.Add("-Dplugin.debug=true");

    // 危险操作：取消启动（需谨慎）
    // ctx.CancelLaunch = true;
}

private void OnAfterLaunch(GameLaunchHookContext ctx)
{
    _context?.LogMessage(PluginLogLevel.Info, $"游戏已启动: {ctx.VersionId}");
    _context?.LogMessage(PluginLogLevel.Debug, $"Java: {ctx.JavaPath}");
}

private void OnCrash(GameLaunchHookContext ctx)
{
    _context?.LogMessage(PluginLogLevel.Error,
        $"游戏崩溃，退出码 {ctx.ExitCode}");
    _context?.LogMessage(PluginLogLevel.Debug, $"崩溃报告:\n{ctx.CrashReport}");

    // 上传崩溃报告到自己的服务器
    UploadCrashReport(ctx.VersionId, ctx.CrashReport);
}

public void OnUnload()
{
    // 钩子在插件卸载时自动清理，无需手动注销
    // 但如需动态卸载，可调用：
    // _context?.UnregisterGameLaunchHook("add-jvm-args");
}
```

### 12. 提交下载请求

将下载请求提交给启动器下载管理器统一调度，复用启动器的多线程下载、断点续传、SHA-1 校验能力。适用于插件更新自身资源、下载整合包、获取模组等场景。

**方法签名**：

```csharp
string RequestDownload(PluginDownloadRequest request);
```

**参数**：`PluginDownloadRequest` 对象

**PluginDownloadRequest 字段**：

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `Url` | `string` | ✅ | 下载 URL（**仅允许 http/https 协议**） |
| `FileName` | `string` | ✅ | 保存文件名（**禁含路径分隔符** `/` `\` `:`） |
| `TargetDirectory` | `string` | ✅ | 目标保存目录（启动器会校验是否在允许范围内） |
| `TaskName` | `string` | ⭕ | 任务显示名称（不传则使用 FileName） |
| `Sha1` | `string?` | ⭕ | SHA-1 校验值（提供时启动器会自动校验完整性） |
| `AutoStart` | `bool` | ⭕ | 是否立即开始下载，默认 `true`；`false` 表示仅创建任务 |

**返回值**：`string` - 下载任务ID；URL/文件名/目录非法或被拒绝时返回空字符串 `""`

**安全约束**：
- 仅允许 `http://` 和 `https://` 协议（拒绝 `file:///`、`ftp://`、`data:` 等）
- 文件名禁含路径分隔符，防止路径遍历攻击
- 目标目录需在启动器允许的范围内（一般为插件数据目录、游戏目录等）
- 回调异常时返回空字符串，不抛出异常

**示例**：

```csharp
public void OnLoad(IPluginContext context)
{
    // 下载插件资源到插件数据目录
    var dataDir = context.PluginDataDirectory;
    var resourcesDir = Path.Combine(dataDir, "resources");
    Directory.CreateDirectory(resourcesDir);

    var taskId = context.RequestDownload(new PluginDownloadRequest
    {
        Url = "https://example.com/plugin-assets/textures.zip",
        FileName = "textures.zip",
        TargetDirectory = resourcesDir,
        TaskName = "插件资源包",
        Sha1 = "a1b2c3d4e5f6...", // 可选，提供时自动校验
        AutoStart = true
    });

    if (string.IsNullOrEmpty(taskId))
    {
        context.LogMessage(PluginLogLevel.Error, "下载请求被拒绝");
        return;
    }

    context.LogMessage(PluginLogLevel.Info, $"下载任务已创建: {taskId}");

    // 订阅下载进度事件
    context.SubscribeEvent(IPluginContext.EventNames.DownloadProgress, OnDownloadProgress);
}

private void OnDownloadProgress(object? eventData)
{
    if (eventData is DownloadProgressEventArgs args)
    {
        _context?.LogMessage(PluginLogLevel.Debug,
            $"[{args.TaskName}] {args.Progress:F1}% - {args.DownloadSpeed / 1024} KB/s");
    }
}
```

**常见拒绝原因**：

| 原因 | 解决方案 |
|------|---------|
| URL 为空或非 http/https 协议 | 检查 URL 拼接，确保以 `http://` 或 `https://` 开头 |
| 文件名含 `/` `\` `:` | 仅传文件名，目录信息放到 `TargetDirectory` |
| 目标目录不在启动器允许范围 | 使用 `context.PluginDataDirectory` 或其子目录 |
| 下载管理器回调未注入 | 一般是启动器初始化未完成，稍后再试 |

---

## 💻 开发流程

### 1. 创建项目

```bash
dotnet new classlib -n YourPlugin -f net8.0

cd YourPlugin

dotnet add reference path/to/ObsMCLauncher.Core.dll
```

### 2. 项目文件配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="path\to\ObsMCLauncher.Core.csproj" />
  </ItemGroup>
</Project>
```

### 3. 实现插件接口

创建 `Plugin.cs`：

```csharp
using ObsMCLauncher.Core.Plugins;
using System;
using System.IO;

namespace YourPlugin
{
    public class Plugin : ILauncherPlugin
    {
        public string Id => "your-plugin-id";
        public string Name => "Your Plugin Name";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "A brief description of your plugin.";
        
        private IPluginContext? _context;
        
        public void OnLoad(IPluginContext context)
        {
            _context = context;
            
            System.Diagnostics.Debug.WriteLine($"[{Name}] 插件已加载");
            
            context.SubscribeEvent("GameLaunched", OnGameLaunched);
            
            context.RegisterHomeCard(
                "example-card",
                "示例卡片",
                "这是一个插件卡片示例",
                "Star"
            );
        }
        
        public void OnUnload()
        {
            _context?.UnregisterHomeCard("example-card");
            System.Diagnostics.Debug.WriteLine($"[{Name}] 插件已卸载");
        }
        
        public void OnShutdown()
        {
            var configPath = Path.Combine(_context!.PluginDataDirectory, "config.json");
            File.WriteAllText(configPath, "{}");
        }
        
        private void OnGameLaunched(object? eventData)
        {
            System.Diagnostics.Debug.WriteLine($"[{Name}] 游戏已启动");
        }
    }
}
```

### 4. 创建 plugin.json

```json
{
  "id": "your-plugin-id",
  "name": "Your Plugin Name",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "A brief description of your plugin.",
  "repository": "https://github.com/yourusername/your-plugin",
  "minLauncherVersion": "1.0.0",
  "dependencies": [],
  "tags": ["Windows", "工具"],
  "category": "utility"
}
```

### 5. 编译插件

```bash
dotnet build -c Release
```

---

## 🧪 测试插件

### 本地测试

1. **找到启动器运行目录**
   - 开发环境：`H:\projects\ObsMCLauncher\bin\Debug\net8.0\`

2. **创建插件文件夹**
   ```
   启动器目录\OMCL\plugins\your-plugin-id\
   ```

3. **复制插件文件**
   - `YourPlugin.dll`
   - `plugin.json`
   - `icon.png`（可选）
   - `README.md`（必需）

4. **重启启动器**

### 调试

使用 Visual Studio 附加到 `ObsMCLauncher.Desktop` 进程进行调试。

---

## 🚀 发布流程

### 1. 准备发布包

```bash
dotnet build -c Release

cd bin/Release/net8.0/

Compress-Archive -Path YourPlugin.dll,plugin.json,README.md -DestinationPath YourPlugin.zip
```

### 2. GitHub Release

1. 创建新 Release
2. Tag: `v1.0.0`
3. 上传 ZIP 文件

### 3. 提交到插件市场

在 [ObsMCLauncher-PluginMarket](https://github.com/mcobs/ObsMCLauncher-PluginMarket) 提交 PR 或 Issue。

---

## 📝 示例插件

### 简单通知插件

```csharp
using ObsMCLauncher.Core.Plugins;

namespace HelloPlugin
{
    public class Plugin : ILauncherPlugin
    {
        public string Id => "hello-plugin";
        public string Name => "Hello Plugin";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "A simple example plugin.";
        
        public void OnLoad(IPluginContext context)
        {
            System.Diagnostics.Debug.WriteLine("[HelloPlugin] Hello from plugin!");
        }
        
        public void OnUnload() { }
        public void OnShutdown() { }
    }
}
```

### 事件订阅插件

```csharp
using ObsMCLauncher.Core.Plugins;
using System.Diagnostics;

namespace EventPlugin
{
    public class Plugin : ILauncherPlugin
    {
        public string Id => "event-plugin";
        public string Name => "Event Plugin";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "Demonstrates event subscription.";
        
        private IPluginContext? _context;
        
        public void OnLoad(IPluginContext context)
        {
            _context = context;
            
            context.SubscribeEvent("GameLaunched", OnGameLaunched);
            context.SubscribeEvent("GameClosed", OnGameClosed);
            
            Debug.WriteLine("[EventPlugin] Subscribed to events");
        }
        
        public void OnUnload()
        {
            Debug.WriteLine("[EventPlugin] Unloaded");
        }
        
        public void OnShutdown()
        {
            Debug.WriteLine("[EventPlugin] Shutdown");
        }
        
        private void OnGameLaunched(object? eventData)
        {
            Debug.WriteLine($"[EventPlugin] Game launched: {eventData}");
        }
        
        private void OnGameClosed(object? eventData)
        {
            Debug.WriteLine($"[EventPlugin] Game closed: {eventData}");
        }
    }
}
```

### 启动钩子插件（使用扩展 API）

演示使用 `RegisterGameLaunchHook` 在游戏启动前追加 JVM 参数、崩溃时上传报告：

```csharp
using System;
using System.IO;
using System.Net.Http;
using ObsMCLauncher.Core.Plugins;

namespace CrashReporterPlugin
{
    public class Plugin : ILauncherPlugin
    {
        public string Id => "crash-reporter";
        public string Name => "Crash Reporter";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "自动收集并上传崩溃报告。";

        private IPluginContext? _context;

        public void OnLoad(IPluginContext context)
        {
            _context = context;
            context.LogMessage(PluginLogLevel.Info, "Crash Reporter 已加载");

            // 启动前追加 JVM 参数（启用堆栈追踪打印）
            context.RegisterGameLaunchHook("add-args",
                GameLaunchPhase.BeforeLaunch, OnBeforeLaunch);

            // 崩溃时上传报告
            context.RegisterGameLaunchHook("upload-report",
                GameLaunchPhase.OnCrash, OnCrash);

            // 启动后展示当前账户信息
            context.RegisterGameLaunchHook("show-account",
                GameLaunchPhase.AfterLaunch, OnAfterLaunch);
        }

        private void OnBeforeLaunch(GameLaunchHookContext ctx)
        {
            ctx.ExtraJvmArguments.Add("-XX:+ShowMessageBoxOnError");
            _context?.LogMessage(PluginLogLevel.Debug,
                $"已追加 JVM 参数，版本: {ctx.VersionId}");
        }

        private void OnAfterLaunch(GameLaunchHookContext ctx)
        {
            var account = _context?.GetCurrentAccount();
            if (account != null)
            {
                _context?.LogMessage(PluginLogLevel.Info,
                    $"以 {account.Username} ({account.AccountType}) 启动 {ctx.VersionId}");
            }
        }

        private void OnCrash(GameLaunchHookContext ctx)
        {
            _context?.LogMessage(PluginLogLevel.Error,
                $"检测到崩溃，退出码 {ctx.ExitCode}");

            var reportPath = Path.Combine(ctx.GameDirectory, "crash-reports",
                $"crash-{DateTime.Now:yyyy-MM-dd_HH.mm.ss}.txt");

            try
            {
                // 保存崩溃报告到游戏目录
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, ctx.CrashReport ?? "(no report)");

                // 上传到自己的服务器
                using var http = new HttpClient();
                var content = new MultipartFormDataContent
                {
                    { new StringContent(ctx.VersionId), "version" },
                    { new StringContent(ctx.CrashReport ?? ""), "report" }
                };
                http.PostAsync("https://your-server.com/api/crash", content).Wait();
            }
            catch (Exception ex)
            {
                _context?.LogMessage(PluginLogLevel.Error, $"上传崩溃报告失败: {ex.Message}");
            }
        }

        public void OnUnload()
        {
            _context?.LogMessage(PluginLogLevel.Info, "Crash Reporter 已卸载");
        }

        public void OnShutdown() { }
    }
}
```

### 版本备份插件（综合使用扩展 API）

演示综合使用 `GetInstalledVersions` / `RequestDownload` / `LogMessage`：

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ObsMCLauncher.Core.Plugins;

namespace BackupPlugin
{
    public class Plugin : ILauncherPlugin
    {
        public string Id => "backup-tool";
        public string Name => "Backup Tool";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "一键备份已安装版本到 ZIP。";

        private IPluginContext? _context;

        public void OnLoad(IPluginContext context)
        {
            _context = context;

            context.RegisterCommand("backup-all", OnBackupAll);
            context.RegisterHomeCard(
                "backup-card",
                "一键备份",
                $"备份全部 {context.GetInstalledVersions().Count} 个版本",
                "💾",
                "command:backup-tool.backup-all"
            );
        }

        private void OnBackupAll(object? payload)
        {
            var ctx = _context!;
            var versions = ctx.GetInstalledVersions();
            ctx.LogMessage(PluginLogLevel.Info, $"开始备份 {versions.Count} 个版本");

            var backupDir = Path.Combine(ctx.PluginDataDirectory, "backups");
            Directory.CreateDirectory(backupDir);

            foreach (var v in versions)
            {
                try
                {
                    var zipPath = Path.Combine(backupDir,
                        $"{v.VersionId}-{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                    ZipFile.CreateFromDirectory(v.VersionDirectory, zipPath);
                    ctx.LogMessage(PluginLogLevel.Info, $"已备份: {v.VersionId}");
                }
                catch (Exception ex)
                {
                    ctx.LogMessage(PluginLogLevel.Error,
                        $"备份失败 {v.VersionId}: {ex.Message}");
                }
            }

            ctx.ShowNotification("备份完成", $"已备份 {versions.Count} 个版本", "success");
        }

        public void OnUnload() { }
        public void OnShutdown() { }
    }
}
```

---

## ⚠️ UI 框架说明

ObsMCLauncher 使用 **Avalonia UI** 框架开发，支持跨平台运行（Windows/macOS/Linux）。

插件开发时请注意：
- 使用 `Path.Combine()` 处理文件路径，确保跨平台兼容性
- UI 操作需在 UI 线程执行，使用 `Avalonia.Threading.Dispatcher.UIThread.Post()`

---

## ❓ 常见问题

### Q: 插件可以访问哪些启动器功能？

A: 插件通过 `IPluginContext` 可以访问：
- 事件订阅和发布
- UI 扩展（标签页、主页卡片）
- 插件数据目录
- 启动器版本信息
- **统一日志写入**（`LogMessage`）
- **已安装版本列表查询**（`GetInstalledVersions`）
- **当前账户信息获取**（`GetCurrentAccount`，不含令牌）
- **游戏启动生命周期钩子**（`RegisterGameLaunchHook`，可拦截启动/追加 JVM 参数/接收崩溃报告）
- **下载请求提交**（`RequestDownload`，复用启动器多线程下载/SHA-1 校验）
- 通知系统、自定义命令

### Q: 插件如何保存数据？

A: 使用 `context.PluginDataDirectory` 获取专属数据目录，在该目录下保存配置和数据文件。

### Q: 插件可以添加新的 UI 页面吗？

A: 可以，使用 `context.RegisterTab()` 在"更多"页面注册标签页，支持传入 Avalonia `Control` 作为自定义 UI。

### Q: 插件可以拦截游戏启动吗？

A: 可以，注册 `GameLaunchPhase.BeforeLaunch` 钩子，在回调中设置 `ctx.CancelLaunch = true` 即可中止启动。后续 `BeforeLaunch` 钩子也会停止调用。请谨慎使用，避免影响用户体验。

### Q: 插件可以获取用户的微软访问令牌吗？

A: **不能**。`GetCurrentAccount()` 仅返回 `Username` / `AccountType` / `UUID` / `IsDefault`，不含任何令牌字段。如需调用微软 API，请引导用户在插件内独立完成 OAuth 授权。

### Q: 插件下载文件会被沙箱限制吗？

A: `RequestDownload` 强制要求 `http://` 或 `https://` 协议、文件名禁含路径分隔符、目标目录需在启动器允许范围内（一般为插件数据目录或游戏目录）。这是为了防止路径遍历和敏感协议攻击。

### Q: 插件钩子触发顺序是怎样的？

A: 同一阶段有多个钩子时，按 `{pluginId}.{hookId}` 字典序触发。`BeforeLaunch` 阶段被某个钩子设为 `CancelLaunch = true` 后，后续 `BeforeLaunch` 钩子不再调用。其他阶段（`AfterLaunch` / `OnExited` / `OnCrash`）的所有钩子都会被调用。

### Q: 插件出错会导致启动器崩溃吗？

A: 不会，启动器会捕获插件异常并隔离错误，只会禁用有问题的插件。所有 API 调用（包括回调内部异常）都由启动器统一 try-catch，不会传播到调用方。

### Q: 如何调试插件？

A: 使用 Visual Studio 附加到 `ObsMCLauncher.Desktop` 进程进行调试。同时推荐使用 `context.LogMessage(PluginLogLevel.Debug, ...)` 输出调试信息，可在启动器的"开发控制台"或日志文件中查看。

---

## 参考资源

- [ObsMCLauncher 官方仓库](https://github.com/mcobs/ObsMCLauncher)
- [.NET 8.0 文档](https://learn.microsoft.com/zh-cn/dotnet/)
- [Avalonia UI 文档](https://docs.avaloniaui.net/)
- [Material Design Icons](https://pictogrammers.com/library/mdi/)

---

## 支持

- **GitHub Issues**: [提交问题](https://github.com/mcobs/ObsMCLauncher/issues)
- **讨论区**: [GitHub Discussions](https://github.com/mcobs/ObsMCLauncher/discussions)
- **黑曜石论坛**: [https://mcobs.cn/](https://mcobs.cn/)

---

## 许可证

本文档采用 [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) 许可协议。

插件开发者可以选择任何开源协议发布插件，推荐使用 MIT License 或 GPL-3.0。

**注意**：ObsMCLauncher 本身采用 GPL-3.0 许可证，如果您的插件与启动器深度集成，可能需要考虑使用兼容的许可证。

---

**祝您开发愉快！**

如有任何问题，欢迎在 GitHub 上提问或参与讨论。
