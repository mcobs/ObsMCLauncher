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
  - [API 总览](#api-总览)
  - [1. 目录、版本与兼容性](#1-目录版本与兼容性)
  - [2. 事件系统](#2-事件系统)
  - [3. UI 扩展](#3-ui-扩展)
    - [3.1 注册标签页](#31-注册标签页)
    - [3.2 注册主页卡片](#32-注册主页卡片)
    - [3.3 自定义命令](#33-自定义命令)
    - [3.4 UI 槽位](#34-ui-槽位)
    - [3.5 任意 UI 访问](#35-任意-ui-访问)
    - [3.6 打开链接与页面跳转](#36-打开链接与页面跳转)
  - [4. 通知系统](#4-通知系统)
  - [5. 游戏启动生命周期钩子](#5-游戏启动生命周期钩子)
  - [6. 下载](#6-下载)
  - [7. 崩溃系统](#7-崩溃系统)
  - [8. 日志与配置](#8-日志与配置)
- [开发流程](#开发流程)
- [测试插件](#测试插件)
- [用户安装插件](#用户安装插件)
- [发布流程](#发布流程)
- [示例插件](#示例插件)
- [UI 框架说明](#ui-框架说明)
- [常见问题](#常见问题)

---

## 🔌 插件系统介绍

ObsMCLauncher 采用基于 .NET Assembly 的插件系统，支持开发者使用 C# 编写插件来扩展启动器功能。

### 插件特性

- ✅ 原生性能，无沙箱限制
- ✅ 完整访问启动器 API
- ✅ 支持 UI 扩展（基于 Avalonia，可挂进既有页面，也可自行遍历修改界面）
- ✅ 崩溃报告读取与内置规则分析（可用于自建分析/上报服务）
- ✅ 崩溃后自动询问、可订阅崩溃事件
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

> 字段名一律 **camelCase**（`minLauncherVersion`，不是 `MinLauncherVersion`）。
> 缺少 `id` / `name` / `version` 的插件会被启动器拒绝加载。

### 字段说明

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `id` | string | ✅ | 插件唯一标识符（小写字母、数字、连字符） |
| `name` | string | ✅ | 插件显示名称 |
| `version` | string | ✅ | 版本号（遵循 SemVer） |
| `author` | string | ✅ | 作者名称 |
| `description` | string | ✅ | 简短描述（不超过 200 字） |
| `repository` | string | ⭕ | 源代码仓库 URL |
| `minLauncherVersion` | string | ⭕ | 最低启动器版本要求（默认 1.0.0），见 [1. 目录、版本与兼容性](#1-目录版本与兼容性) |
| `dependencies` | array | ⭕ | 依赖的其他插件ID列表 |
| `tags` | array | ⭕ | 标签列表，支持平台标签：`Windows`、`Linux`、`macOS` |
| `category` | string | ⭕ | 分类ID |
| `homepage` | string | ⭕ | 插件主页 URL |
| `license` | string | ⭕ | 开源协议 |
| `icon` | string | ⭕ | 图标文件名（默认 icon.png） |

> 程序集查找：启动器按 `{插件ID}.dll` 查找入口程序集，找不到时回退到插件目录内任意 `.dll`。
> 是否启用由插件目录下的 `.disabled` 标记文件决定，见 [用户安装插件](#用户安装插件)。

### 加载机制

plugin.json 里**没有入口字段**，启动器也不需要你声明入口类：

1. 扫描 `OMCL\plugins\` 下的每个子文件夹
2. 读 `plugin.json` 并校验：`id` 与文件夹名一致、格式合规、不与已加载插件重复、`minLauncherVersion` 不高于当前启动器、声明的依赖插件已加载
3. 要求根目录有 `README.md`（缺失会被判为加载失败）
4. 定位程序集 `{插件ID}.dll`（找不到则用目录里第一个 `.dll`）；目录里有 `.disabled` 文件则跳过加载
5. `Assembly.LoadFrom` 载入后，**取程序集中第一个实现了 `ILauncherPlugin` 的具体类**（非接口、非抽象），用 `Activator.CreateInstance` 创建实例，再调用 `OnLoad(context)`

因此有两条硬约束：

- 插件主类必须有 **public 无参构造函数**；
- **一个程序集只会被取一个插件类**——想在一个 DLL 里塞多个插件是不行的，请拆成多个插件文件夹。

校验失败、缺少 README、找不到插件类、或 `OnLoad` 抛异常时，启动器会在插件目录下写入 `.disabled` 标记，下次启动直接跳过（删掉该文件可重试）。
注意依赖是按文件夹枚举顺序加载的，所以 `dependencies` 里声明的插件若排在后面，依赖方会首次加载失败被禁用，重启一次通常即可正常。

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

通过插件上下文访问启动器功能。完整定义见 [`ObsMCLauncher.Core/Plugins/IPluginContext.cs`](ObsMCLauncher.Core/Plugins/IPluginContext.cs)，
下面各章按使用场景逐一说明。

```csharp
namespace ObsMCLauncher.Core.Plugins
{
    public interface IPluginContext
    {
        // 事件名称常量：IPluginContext.EventNames.GameLaunched 等
        public static class EventNames { /* ... */ }

        // 目录、版本（见 1）
        string LauncherVersion { get; }
        string PluginDataDirectory { get; }
        string LauncherBaseDirectory { get; }
        string LauncherDataDirectory { get; }
        string GameDirectory { get; }
        int ApiVersion { get; }

        // 事件（见 2）
        void SubscribeEvent(string eventName, Action<object?> handler);
        void UnsubscribeEvent(string eventName, Action<object?> handler);
        void PublishEvent(string eventName, object? eventData);

        // UI 扩展（见 3）
        void RegisterTab(string title, string tabId, string? icon = null, object? payload = null);
        void RegisterTab(string title, string tabId, object? customContent, string? icon = null, object? payload = null);
        void UnregisterTab(string tabId);
        void RegisterHomeCard(string cardId, string title, string description,
            string? icon = null, string? commandId = null, object? payload = null);
        void RegisterHomeCard(string cardId, string title, string description,
            string? icon, string? commandId, object? payload, HomeCardSize defaultSize);
        void UnregisterHomeCard(string cardId);
        void RegisterCommand(string commandId, Action<object?> handler);
        void UnregisterCommand(string commandId);
        bool AddSlotContent(string slotId, string itemId, object content, int order = 0);
        bool RemoveSlotContent(string slotId, string itemId);
        void ClearSlotContent(string slotId);
        IReadOnlyList<string> GetSlotIds();
        object? GetSlotHost(string slotId);
        object? GetUiRoot();
        object? TryFindControlByName(string name);
        void RunOnUiThread(Action action);
        bool OpenUrl(string url);
        void NavigateTo(string page);

        // 通知（见 4）
        string ShowNotification(string title, string message, string type = "info", int? durationSeconds = null);
        void UpdateNotification(string notificationId, string message, double? progress = null);
        void CloseNotification(string notificationId);

        // 启动生命周期钩子（见 5）
        void RegisterGameLaunchHook(string hookId, GameLaunchPhase phase, Action<GameLaunchHookContext> handler);
        void UnregisterGameLaunchHook(string hookId);
        void RegisterGameLaunchHookAsync(string hookId, GameLaunchPhase phase, Func<GameLaunchHookContext, Task> handler);
        void UnregisterGameLaunchHookAsync(string hookId);

        // 下载（见 6）
        string RequestDownload(PluginDownloadRequest request);
        PluginDownloadTaskStatus? GetDownloadTaskStatus(string taskId);

        // 崩溃（见 7）
        IReadOnlyList<PluginCrashReportInfo> GetCrashReports(string? versionId = null);
        PluginCrashAnalysis? AnalyzeCrashReport(string reportPath);
        string? ReadCrashReportText(string reportPath, int maxChars = 200_000, bool sanitize = true);
        string SanitizeCrashReportText(string text);
        PluginSlotContext? GetActiveCrashContext();

        // 日志与配置（见 8）
        void LogMessage(PluginLogLevel level, string message);
        T? GetConfig<T>();
        void SaveConfig<T>(T config);
        IReadOnlyList<PluginVersionInfo> GetInstalledVersions();
        PluginAccountInfo? GetCurrentAccount();
    }
}
```

---

## 🔧 API 参考

### API 总览

| 分组 | API | 一句话说明 | 章节 |
|------|-----|-----------|------|
| 目录 | `PluginDataDirectory` / `LauncherBaseDirectory` / `LauncherDataDirectory` / `GameDirectory` | 插件数据、启动器基础、启动器数据、当前游戏目录 | [1](#1-目录版本与兼容性) |
| 版本 | `LauncherVersion` / `ApiVersion` | 完整版本字符串 / 插件 API 版本（主版本号） | [1](#1-目录版本与兼容性) |
| 事件 | `SubscribeEvent` / `UnsubscribeEvent` / `PublishEvent` | 订阅、退订、发布事件 | [2](#2-事件系统) |
| UI | `RegisterTab` / `UnregisterTab` | 在「更多」页增删标签页 | [3.1](#31-注册标签页) |
| UI | `RegisterHomeCard` / `UnregisterHomeCard` | 在主页增删卡片 | [3.2](#32-注册主页卡片) |
| UI | `RegisterCommand` / `UnregisterCommand` | 注册供卡片点击触发的命令 | [3.3](#33-自定义命令) |
| UI | `AddSlotContent` / `RemoveSlotContent` / `ClearSlotContent` / `GetSlotIds` / `GetSlotHost` | 把控件挂进启动器预留槽位 | [3.4](#34-ui-槽位) |
| UI | `GetUiRoot` / `TryFindControlByName` / `RunOnUiThread` | 不受槽位限制地访问与修改任意 UI | [3.5](#35-任意-ui-访问) |
| UI | `OpenUrl` / `NavigateTo` | 打开外部链接 / 跳转内部页面 | [3.6](#36-打开链接与页面跳转) |
| 通知 | `ShowNotification` / `UpdateNotification` / `CloseNotification` | 显示、更新、关闭通知 | [4](#4-通知系统) |
| 钩子 | `RegisterGameLaunchHook(Async)` / `UnregisterGameLaunchHook(Async)` | 启动前/启动后/退出/崩溃时回调 | [5](#5-游戏启动生命周期钩子) |
| 下载 | `RequestDownload` / `GetDownloadTaskStatus` | 提交下载请求、轮询任务状态 | [6](#6-下载) |
| 崩溃 | `GetCrashReports` / `AnalyzeCrashReport` / `ReadCrashReportText` / `SanitizeCrashReportText` / `GetActiveCrashContext` | 列出、分析、读取、脱敏崩溃报告 | [7](#7-崩溃系统) |
| 日志 | `LogMessage` | 写入启动器统一日志 | [8](#8-日志与配置) |
| 配置 | `GetConfig<T>` / `SaveConfig<T>` | 读写插件自己的 config.json | [8](#8-日志与配置) |
| 查询 | `GetInstalledVersions` / `GetCurrentAccount` | 已安装版本列表 / 当前账户（不含令牌） | [8](#8-日志与配置) |

> 所有 API 调用（包括回调内部抛出的异常）都由启动器统一 try-catch，不会传播到调用方。

### 1. 目录、版本与兼容性

#### 目录 API

`IPluginContext` 提供以下目录 API（均已正确处理 Velopack 部署，不会返回会被更新整体替换的 `current` 目录）：

| API | 说明 |
| --- | --- |
| `PluginDataDirectory` | 当前插件的专属数据目录（`<启动器基础目录>/OMCL/plugins/{插件ID}`），插件配置和数据应保存在这里 |
| `LauncherBaseDirectory` | 启动器基础目录（Velopack 安装模式下自动定位到 `current` 的父级） |
| `LauncherDataDirectory` | 启动器数据目录（`<启动器基础目录>/OMCL`，存放启动器配置/账户/缓存） |
| `GameDirectory` | 当前激活的游戏目录（`.minecraft` 根目录，随用户在设置中的切换实时变化） |

```csharp
public void OnLoad(IPluginContext context)
{
    // 1. 插件自己的数据目录（推荐：插件数据一律放在这里）
    string dataDir = context.PluginDataDirectory;

    var configPath = Path.Combine(dataDir, "config.json");
    File.WriteAllText(configPath, "{}");

    var dataFolder = Path.Combine(dataDir, "data");
    Directory.CreateDirectory(dataFolder);

    // 2. 启动器基础目录 / 数据目录
    string baseDir = context.LauncherBaseDirectory;   // 例如 .../ObsMCLauncher/
    string omclDir = context.LauncherDataDirectory;   // 例如 .../ObsMCLauncher/OMCL

    // 3. 当前游戏目录（.minecraft）
    string gameDir = context.GameDirectory;
    var modsDir = Path.Combine(gameDir, "mods");
}
```

> ⚠️ 生产环境下启动器运行在 Velopack 的 `current` 子目录中（更新时该目录会被整体替换）。
> 上述 API 返回的路径都已自动跳出 `current`。插件**不要**自行拼接
> `AppContext.BaseDirectory` 来定位数据目录，否则数据会写入 `current` 内并在更新时丢失。

#### 版本与兼容性

```csharp
// 完整版本字符串
string version = context.LauncherVersion;          // 如 "1.2.3"
if (new Version(version) < new Version("1.1.0"))
{
    // 启动器版本过低
}

// 插件 API 版本 = 启动器主版本号（v1.2.3 → 1；带 -preview / -beta 后缀时取主干第一段）
int api = context.ApiVersion;
```

版本语义：

| 变化 | 含义 |
|------|------|
| **主版本递进**（1 → 2） | 插件 API **可能发生破坏性变更**（删改已有成员），需要重新适配 |
| 次版本 / 修订号变化 | 只**新增**成员，不改动已有成员，已编译的插件继续可用 |

因此插件侧的判断很简单：

```csharp
// 只在需要的大版本上启用某功能
if (_context.ApiVersion >= 1)
{
    var reports = _context.GetCrashReports();
}
```

> 新能力只会往 `IPluginContext` **增加**成员（插件只是消费方，不需要自己实现该接口），
> 所以小版本升级不会让现有插件失效；出现破坏性变更时主版本会递进。

反方向——"本插件要求启动器至少多新"——在 `plugin.json` 里声明（字段名 camelCase）：

```json
{
  "id": "my.plugin",
  "minLauncherVersion": "1.2.0"
}
```

启动器加载插件时会据此拒绝版本过旧的插件。若某个新 API 在旧启动器上不存在，调用它会在运行时抛出
`MissingMethodException`；不想写 try/catch 的话，就用上面的 `minLauncherVersion` 把门槛立起来。

### 2. 事件系统

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
| `GameLaunched` | `EventNames.GameLaunched` | 游戏启动成功 | `string`（versionId） |
| `GameClosed` | `EventNames.GameClosed` | 游戏进程关闭 | `int`（exitCode） |
| `VersionDownloaded` | `EventNames.VersionDownloaded` | 版本下载就绪 | `VersionInstalledEventArgs` |
| `VersionInstalling` | `EventNames.VersionInstalling` | 版本安装开始 | `VersionInstallingEventArgs` |
| `VersionInstalled` | `EventNames.VersionInstalled` | 版本安装完成/失败 | `VersionInstalledEventArgs` |
| `AccountChanged` | `EventNames.AccountChanged` | 账户变更 | `AccountChangedEventArgs` |
| `DownloadProgress` | `EventNames.DownloadProgress` | 下载进度更新 | `DownloadProgressEventArgs` |
| `CrashDetected` | `EventNames.CrashDetected` | 崩溃已确认（报告落盘状态已确定） | `PluginCrashReportInfo`，见 [7. 崩溃系统](#7-崩溃系统) |

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

插件还可以退订事件，避免不再需要时继续收到通知：

```csharp
public void OnLoad(IPluginContext context)
{
    context.SubscribeEvent(IPluginContext.EventNames.GameLaunched, OnGameLaunched);
    // ...
}

private void OnGameLaunched(object? eventData) { }

private void DisableSubscription(IPluginContext context)
{
    context.UnsubscribeEvent(IPluginContext.EventNames.GameLaunched, OnGameLaunched);
}
```

> 说明：插件卸载或禁用时，启动器会自动清理该插件的所有事件订阅，无需手动退订。
> `UnsubscribeEvent` 的 `handler` 须与订阅时传入的是同一个方法引用（实例方法会隐式捕获 `this`）。

### 3. UI 扩展

往启动器界面里加东西有四档自由度，越往下越自由、兼容性风险也越高：

| 方式 | 位置 | 生命周期 | 兼容性 |
|------|------|---------|--------|
| [3.1 标签页](#31-注册标签页) | 「更多」页新增一个 Tab | 启动器管理 | 稳定 |
| [3.2 主页卡片](#32-注册主页卡片) | 主页卡片网格 | 启动器管理 | 稳定 |
| [3.4 UI 槽位](#34-ui-槽位) | 既有页面的预留容器 | 启动器管理 | 稳定（槽位 id 有保证） |
| [3.5 任意 UI 访问](#35-任意-ui-访问) | 视觉树上任意位置 | 插件自己负责 | 随版本变化，需容错 |

四种方式都需要给插件项目加 Avalonia 包引用才能创建控件：

```xml
<ItemGroup>
  <ProjectReference Include="path\to\ObsMCLauncher.Core.csproj" />
  <PackageReference Include="Avalonia" Version="11.3.11" />
</ItemGroup>
```

#### 3.1 注册标签页

插件可以在"更多"页面添加自己的标签页：

```csharp
// 注册普通标签页（显示默认文本信息）
public void OnLoad(IPluginContext context)
{
    context.RegisterTab(
        "我的插件",           // 标签页标题
        "my-plugin-tab",     // 标签页ID（唯一）
        "Star",              // 图标名称（可选，Material Design 图标名）
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
- 传入 `Control` 对象时，标签页直接渲染该控件
- 不传 `Control` 时，标签页显示默认文本信息
- `UnregisterTab(tabId)` 可主动注销

#### 3.2 注册主页卡片

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
- `command:{pluginId}.{commandId}` - 执行插件注册的自定义命令，见 [3.3](#33-自定义命令)
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

**指定卡片默认尺寸**（重载）：

```csharp
context.RegisterHomeCard(
    "stats-card",                 // 卡片ID
    "服务器状态",                   // 卡片标题
    "查看服务器在线情况",            // 卡片描述
    "🌐",                          // 图标
    "command:my-plugin.check",     // 命令ID
    null,                          // 自定义数据
    HomeCardSize.Large             // 默认尺寸档位
);
```

尺寸档位 `HomeCardSize`（命名空间 `ObsMCLauncher.Core.Models`）：

| 档位 | 说明 |
| --- | --- |
| `Small` | 紧凑卡片 |
| `Medium` | 标准宽度（默认） |
| `Large` | 加宽，约两倍标准宽 |
| `Fill` | 占满整行 |

说明：
- 尺寸档位只是**默认值**，用户可以在"设置 → 主页自定义"中随意调整每张卡片的实际尺寸与位置
- 主页支持自定义布局：用户可以添加、删除、拖动卡片组件（账号选择、版本选择、启动按钮等操作区为固定结构，不参与自定义）
- 未指定尺寸的旧签名调用等同于 `Medium`

#### 3.3 自定义命令

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

#### 3.4 UI 槽位

把自绘控件挂进启动器既有页面的预留位置。

当前**保证有宿主容器**的槽位：

| slotId | 位置 |
|--------|------|
| `crash.dialog.actions` | 崩溃弹窗的按钮行 |
| `crash.dialog.analysis.after` | 崩溃弹窗里分析结论的下方 |
| `crash.page.analysis.after` | 「更多 → 崩溃分析」页分析详情下方 |
| `crash.page.toolbar` | 「更多 → 崩溃分析」页顶部工具栏 |

```csharp
using Avalonia.Controls;

public void OnLoad(IPluginContext context)
{
    var button = new Button { Content = "用我的插件分析" };
    button.Click += (_, _) => AnalyzeCurrent(context);

    // 注册进槽位；order 小的排前面
    context.AddSlotContent("crash.dialog.actions", "analyze-btn", button, order: -1);

    // 也可以拿到宿主容器自己增删（容器里同时有启动器控件和插件内容）
    if (context.GetSlotHost("crash.dialog.actions") is Panel host)
    {
        host.Children.Add(new TextBlock { Text = "插件已就绪" });
    }
}

public void OnUnload()
{
    // 卸载时清理：不清理的话会留下控件引用
    _context.ClearSlotContent("crash.dialog.actions");
}
```

说明：

- **slotId 是开放字符串**：不在上表里的 id 也能注册（记一条警告），但只有当该 id 的宿主出现时才会渲染。想完全自己决定挂在哪，请用 [3.5](#35-任意-ui-访问) 的 `GetUiRoot()`。
- 槽位内容的 `DataContext` 若为空，启动器会填一个 `PluginSlotContext`（`SlotId` / `CrashReportPath` / `VersionId`），插件据此知道"当前是哪份报告"；也可用 `GetActiveCrashContext()` 主动查询。
- 插件禁用 / 卸载时，启动器会自动清理其所有槽位内容。

#### 3.5 任意 UI 访问

**不需要启动器预先开槽位**——拿到 UI 根后，插件可以自己遍历视觉树、往任意容器增删控件、改任意控件属性：

```csharp
using Avalonia.Controls;
using Avalonia.VisualTree;

// 主窗口（Avalonia Window）；没有窗口时返回 null
if (_context.GetUiRoot() is Window root)
{
    // 按 x:Name 找控件（名字由启动器维护，比自己遍历稳一点）
    if (_context.TryFindControlByName("CrashDialogHeader") is TextBlock header)
    {
        header.Text = "插件改过的标题";
    }

    // 自己遍历：把某个按钮改成不可见
    var target = root.GetVisualDescendants()
                     .OfType<Button>()
                     .FirstOrDefault(b => b.Content is "打开游戏日志文件夹");
    if (target != null) target.IsVisible = false;
}

// 从别的线程操作 UI 时，用这个把回调调度到 UI 线程
_context.RunOnUiThread(() => MyUpdate());
```

风险与建议（这条路径是"完全自由"的，代价由插件承担）：

- 页面结构可能随版本变化，**找不到控件就跳过**，不要假设它一定存在；
- 改动启动器自己的控件（隐藏、改文案）会影响用户，请谨慎；
- 所有 UI 操作都要在 UI 线程执行（用 `RunOnUiThread`）；
- 插件卸载时不会自动撤销这类修改，请在 `OnUnload` 里还原。

#### 3.6 打开链接与页面跳转

```csharp
// 用系统默认浏览器打开链接（仅 http/https）
bool ok = _context.OpenUrl("https://example.com");

// 跳转到启动器内部页面：home / multiplayer / resources / accounts / versions / settings / more
_context.NavigateTo("resources");
```

> 卡片点击也能做到同样的事，用 `url:` / `navigate:` 命令 ID，见 [3.2](#32-注册主页卡片)。

### 4. 通知系统

插件可以显示、更新和关闭通知：

```csharp
public void OnLoad(IPluginContext context)
{
    // 显示简单通知（默认时长后自动关闭）
    context.ShowNotification("提示", "操作成功", "success");

    // 显示错误通知（5秒后关闭）
    context.ShowNotification("错误", "操作失败", "error", 5);

    // 显示进度通知（不自动关闭，需手动 CloseNotification）
    var notifId = context.ShowNotification("下载中", "正在下载...", "progress", 0);

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

**持续时间**（`durationSeconds`）：

| 取值 | 行为 |
|------|------|
| 不传 / `null` | 按类型取默认时长（进度类默认不自动关闭；其余约 3–5 秒，并尊重用户的通知设置） |
| 正数 | 指定秒数后自动关闭 |
| `0` 或负数 | **不自动关闭**，需显式 `CloseNotification` |

### 5. 游戏启动生命周期钩子

注册游戏启动生命周期钩子，在启动前/启动后/退出/崩溃时执行自定义逻辑。可在启动前修改 JVM/游戏参数，或拦截启动。

**方法签名**：

```csharp
void RegisterGameLaunchHook(string hookId, GameLaunchPhase phase, Action<GameLaunchHookContext> handler);
void UnregisterGameLaunchHook(string hookId);

// 回调需要执行耗时/网络操作时用异步版本，避免阻塞启动流程
void RegisterGameLaunchHookAsync(string hookId, GameLaunchPhase phase, Func<GameLaunchHookContext, Task> handler);
void UnregisterGameLaunchHookAsync(string hookId);
```

**参数**：

| 参数 | 类型 | 说明 |
|------|------|------|
| `hookId` | `string` | 钩子唯一标识（在插件内唯一） |
| `phase` | `GameLaunchPhase` | 触发阶段（见下表） |
| `handler` | `Action<GameLaunchHookContext>` / `Func<GameLaunchHookContext, Task>` | 回调函数，接收钩子上下文 |

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
| `CrashReport` | `string?` | 崩溃报告内容（仅 `OnCrash` 有效，可能为 null） |
| `CancelLaunch` | `bool` | 设为 true 中止启动（仅 `BeforeLaunch` 有效） |
| `ExtraJvmArguments` | `List<string>` | 追加 JVM 参数（仅 `BeforeLaunch` 有效） |
| `ExtraGameArguments` | `List<string>` | 追加游戏参数（仅 `BeforeLaunch` 有效） |

**同步 vs 异步**：同步钩子会在启动流程中被阻塞等待，**回调里不要做网络请求、大量 IO 或 `.Wait()`**，
这类操作请改用 `RegisterGameLaunchHookAsync`。两者的其它规则（触发顺序、`CancelLaunch` 拦截、异常隔离）一致，
同步与异步钩子按 `{pluginId}.{hookId}` 字典序混合触发。

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

    // 崩溃时上传报告（需要网络 → 异步钩子）
    context.RegisterGameLaunchHookAsync("upload-crash",
        GameLaunchPhase.OnCrash, OnCrashAsync);
}

private void OnBeforeLaunch(GameLaunchHookContext ctx)
{
    // 为低版本 Minecraft 强制使用 Java 8（演示用，实际应通过 JavaDetector）
    if (ctx.McVersion.StartsWith("1.12."))
    {
        ctx.ExtraJvmArguments.Add("-Djava.util.Arrays.useLegacyMergeSort=true");
    }

    // 危险操作：取消启动（需谨慎）
    // ctx.CancelLaunch = true;
}

private void OnAfterLaunch(GameLaunchHookContext ctx)
{
    _context?.LogMessage(PluginLogLevel.Info, $"游戏已启动: {ctx.VersionId}");
    _context?.LogMessage(PluginLogLevel.Debug, $"Java: {ctx.JavaPath}");
}

private async Task OnCrashAsync(GameLaunchHookContext ctx)
{
    _context?.LogMessage(PluginLogLevel.Error, $"游戏崩溃，退出码 {ctx.ExitCode}");

    // 需要确保拿到报告文件时，请改用 CrashDetected 事件，见「7. 崩溃系统」
    if (string.IsNullOrEmpty(ctx.CrashReport)) return;

    await UploadCrashReportAsync(ctx.VersionId, ctx.CrashReport);
}

public void OnUnload()
{
    // 钩子在插件卸载时自动清理，无需手动注销
    // 但如需动态卸载，可调用：
    // _context?.UnregisterGameLaunchHook("add-jvm-args");
    // _context?.UnregisterGameLaunchHookAsync("upload-crash");
}
```

### 6. 下载

将下载请求提交给启动器下载管理器统一调度，复用启动器的多线程下载、断点续传、SHA-1 校验能力。适用于插件更新自身资源、下载整合包、获取模组等场景。

**方法签名**：

```csharp
string RequestDownload(PluginDownloadRequest request);
PluginDownloadTaskStatus? GetDownloadTaskStatus(string taskId);
```

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

**查询任务状态 / 订阅进度**：

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

    // ① 订阅下载进度事件（推荐）
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

// ② 也可以按 taskId 主动轮询
var status = _context.GetDownloadTaskStatus(taskId);
if (status != null)
{
    // status.Status: Downloading / Completed / Failed / Cancelled
    _context.LogMessage(PluginLogLevel.Info, $"{taskId} -> {status.Status} {status.Progress:F0}%");
}
```

**常见拒绝原因**：

| 原因 | 解决方案 |
|------|---------|
| URL 为空或非 http/https 协议 | 检查 URL 拼接，确保以 `http://` 或 `https://` 开头 |
| 文件名含 `/` `\` `:` | 仅传文件名，目录信息放到 `TargetDirectory` |
| 目标目录不在启动器允许范围 | 使用 `context.PluginDataDirectory` 或其子目录 |
| 下载管理器回调未注入 | 一般是启动器初始化未完成，稍后再试 |

### 7. 崩溃系统

三个入口，按需选择：

| | `GameLaunchPhase.OnCrash` 钩子 | `EventNames.CrashDetected` 事件 | 崩溃数据 API |
|---|---|---|---|
| 触发时机 | 游戏进程一退出就触发 | 启动器**等过报告落盘**之后触发（约 1–3 秒） | 插件主动调用 |
| 报告路径 | `ctx.CrashReport`，**可能是 null**（报告还没写完） | `PluginCrashReportInfo.ReportFound` 明确告诉有没有找到 | `GetCrashReports()` 列出历史报告 |
| 适合 | 只想"知道游戏崩了" | 需要**拿到报告文件**才能干活 | 主动遍历/分析历史报告 |

```csharp
// ① 启动钩子：进程一退出就回调，ctx.CrashReport 是尽力查找的结果
_context.RegisterGameLaunchHookAsync("my-crash-hook", GameLaunchPhase.OnCrash, async ctx =>
{
    _context.LogMessage(PluginLogLevel.Warning, $"游戏崩溃，退出码 {ctx.ExitCode}");
    if (ctx.CrashReport != null) { /* 报告可能还没写完，这里只是尽力 */ }
});

// ② CrashDetected：报告落盘状态已确定
_context.SubscribeEvent(IPluginContext.EventNames.CrashDetected, data =>
{
    if (data is PluginCrashReportInfo info && info.ReportFound)
    {
        var text = _context.ReadCrashReportText(info.ReportPath);
        // 拿到内容后做你自己的处理
    }
});
```

**崩溃数据 API**（都在启动器内核中完成，无界面依赖、不会弹窗）：

```csharp
// 列出崩溃报告；传版本 ID 限定，传 null 查全部
IReadOnlyList<PluginCrashReportInfo> reports = _context.GetCrashReports();

// 用启动器内置规则引擎分析（同步、纯本地、无网络）
PluginCrashAnalysis? analysis = _context.AnalyzeCrashReport(reports[0].ReportPath);

// 读原文（默认脱敏、默认最多 20 万字符，上限 2,000,000）
string? text = _context.ReadCrashReportText(reports[0].ReportPath, maxChars: 100_000);

// 手动脱敏：发往外部服务前建议再过一遍
string safe = _context.SanitizeCrashReportText(text!);

// 取"用户当前正在看的崩溃上下文"（弹窗 / 崩溃分析页当前选中的报告）
PluginSlotContext? active = _context.GetActiveCrashContext();
```

**返回的数据（精简字段）**：

| 类型 | 字段 |
|------|------|
| `PluginCrashReportInfo` | `ReportPath`、`FileName`、`VersionId`、`Kind`（`Minecraft` / `JvmFatalError`）、`CreatedTime`、`SizeBytes`、`ReportFound`、`ExitCode` |
| `PluginCrashAnalysis` | `Headline`、`MinecraftVersion`、`LoaderInfo`、`JavaVersion`、`OperatingSystem`、`CrashTime`、`Description`、`ExceptionSummary`、`Causes`、`SuspectedMods`、`RawPreview` |
| `PluginCrashCause` | `Category`、`Title`、`Evidence`、`Suggestion`、`Confidence`（`High` / `Medium` / `Low`） |

注意：

- `ReadCrashReportText` 的 `sanitize` 默认 `true`，会抹掉用户名、`--accessToken`、用户目录路径等；**先脱敏再截断**，所以返回长度不会超过 `maxChars`。要原始内容请显式传 `sanitize: false`。
- 脱敏只处理"凭据 / 路径"这类**有明确模式**的字段，不会抹掉正文里任意出现的用户名。发给第三方前请自行检查。
- 报告文件可能已被用户删除，`ReadCrashReportText` / `AnalyzeCrashReport` 找不到文件时分别返回 `null`，请判空。
- 崩溃后启动器**总是**弹出询问是否分析的弹窗（没有开关），与插件的行为互不影响。

### 8. 日志与配置

#### 日志写入

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

#### 插件配置读写

配置存于插件数据目录下的 `config.json`：

```csharp
public class MyConfig
{
    public string Name { get; set; } = "";
    public int Max { get; set; } = 10;
}

// 读取（文件不存在或解析失败时返回 default）
var cfg = _context.GetConfig<MyConfig>() ?? new MyConfig();

// 写入
_context.SaveConfig(new MyConfig { Name = "demo", Max = 20 });
```

#### 查询已安装版本

获取启动器中已安装的 Minecraft 版本只读列表，用于插件展示版本信息、按版本执行操作（如备份、迁移、统计）。

```csharp
IReadOnlyList<PluginVersionInfo> GetInstalledVersions();
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `VersionId` | `string` | 版本ID（如 `1.20.1-Forge`） |
| `McVersion` | `string` | Minecraft 原版版本号（如 `1.20.1`） |
| `LoaderType` | `string` | 加载器类型：`vanilla` / `forge` / `fabric` / `quilt` / `neoforge` / `optifine` |
| `VersionDirectory` | `string` | 版本目录绝对路径 |
| `LastPlayed` | `DateTime?` | 最后游玩时间；从未游玩为 null |

**安全说明**：仅返回版本元数据，不包含任何账户、令牌、JVM 参数等敏感字段。回调异常或未注入时返回空列表。

```csharp
var versions = context.GetInstalledVersions();
context.LogMessage(PluginLogLevel.Info, $"共 {versions.Count} 个已安装版本");

// 仅备份 Forge 版本
foreach (var v in versions.Where(v => v.LoaderType == "forge"))
{
    BackupVersion(v.VersionDirectory);
}
```

#### 查询当前账户

```csharp
PluginAccountInfo? GetCurrentAccount();
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `AccountId` | `string` | 账户内部ID |
| `Username` | `string` | 用户名（离线模式为玩家自定义名，微软模式为 Xbox GT） |
| `AccountType` | `string` | 账户类型：`Offline` / `Microsoft` / `Yggdrasil` |
| `UUID` | `string` | Minecraft UUID（不带连字符的 32 位十六进制） |
| `IsDefault` | `bool` | 是否为默认账户 |

**安全说明**：**不返回任何令牌字段**（不含 Access Token / Xbox Live Token / Minecraft Services Token）。如需发起微软 API 请求，请让用户自行授权。

```csharp
var account = context.GetCurrentAccount();
if (account == null)
{
    context.LogMessage(PluginLogLevel.Warning, "未选中账户");
    return;
}

context.LogMessage(PluginLogLevel.Info, $"当前账户: {account.Username} ({account.AccountType})");
```

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

用到 UI 扩展（[3. UI 扩展](#3-ui-扩展)）时再加 Avalonia 包引用。

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

            context.LogMessage(PluginLogLevel.Info, $"[{Name}] 插件已加载");

            // 推荐用 EventNames 常量，避免拼写错误
            context.SubscribeEvent(IPluginContext.EventNames.GameLaunched, OnGameLaunched);

            context.RegisterHomeCard(
                "example-card",
                "示例卡片",
                "这是一个插件卡片示例",
                "🌟"
            );
        }

        public void OnUnload()
        {
            _context?.UnregisterHomeCard("example-card");
            _context?.LogMessage(PluginLogLevel.Info, $"[{Name}] 插件已卸载");
        }

        public void OnShutdown()
        {
            // 用配置 API 落盘，省得自己拼路径
            _context?.SaveConfig(new { LastExit = DateTime.Now });
        }

        private void OnGameLaunched(object? eventData)
        {
            _context?.LogMessage(PluginLogLevel.Debug, $"[{Name}] 游戏已启动: {eventData}");
        }
    }
}
```

### 4. 创建 plugin.json

字段名与格式见 [插件结构 › plugin.json 格式](#pluginjson-格式)，复制到插件项目根目录即可。

### 5. 编译插件

```bash
dotnet build -c Release
```

---

## 🧪 测试插件

### 本地测试

1. **找到启动器运行目录**
   - 开发环境：`ObsMCLauncher.Desktop` 的编译输出目录（如 `{仓库}/ObsMCLauncher.Desktop/bin/Debug/net8.0/`）
   - 正式环境：启动器安装目录下的 `current` 同级目录（见 [1. 目录、版本与兼容性](#1-目录版本与兼容性)）

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

使用 Visual Studio 附加到 `ObsMCLauncher.Desktop` 进程进行调试。配合 `context.LogMessage(PluginLogLevel.Debug, ...)` 输出调试信息，可在启动器的"开发控制台"或日志文件中查看。

---

## 📥 用户安装插件

1. 从插件市场或 Release 页面下载插件 ZIP
2. 解压到 `OMCL\plugins\` 下，**文件夹名必须与插件 ID 一致**：
   ```
   OMCL\plugins\your-plugin-id\
       ├── your-plugin-id.dll
       ├── plugin.json
       └── README.md
   ```
3. 重启启动器

**临时禁用**：在插件文件夹里放一个名为 `.disabled` 的空文件，启动器会跳过该插件；删掉这个文件即可重新启用。
启动器在插件加载失败时也会自动写入 `.disabled`，避免反复崩溃。

**卸载**：在启动器里删除插件时，启动器会先热卸载插件实例（调用 `OnUnload` 并清理它注册的命令 / 钩子 / 事件订阅 / 槽位内容），再删除插件文件夹。
由于程序集是 `Assembly.LoadFrom` 载入的、DLL 在进程生命周期内被占用，删除有时会失败——此时启动器会在插件目录留下一个
`.delete_on_restart` 标记，**下次启动扫描插件时删掉整个目录**。

| 标记文件 | 含义 |
|----------|------|
| `.disabled` | 跳过加载（下次启动生效）；删掉即可恢复 |
| `.delete_on_restart` | 下次启动扫描时删除整个插件目录（卸载时文件被占用才会留下） |

> 对插件作者的含义：不要在 `OnUnload` 里假设文件还能访问或还能重新加载；也不要指望卸载后程序集能被真正释放——同一个进程里同一插件不会被二次加载。

---

## 🚀 发布流程

### 1. 准备发布包

```bash
dotnet build -c Release
```

Windows（PowerShell）：

```powershell
Compress-Archive -Path YourPlugin.dll,plugin.json,README.md -DestinationPath YourPlugin.zip
```

macOS / Linux：

```bash
zip YourPlugin.zip YourPlugin.dll plugin.json README.md
```

> ZIP 里的文件应在**根目录**——用户解压后应直接得到插件文件夹，而不是多套一层目录。

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
            context.ShowNotification("Hello", "插件已加载", "success");
        }

        public void OnUnload() { }
        public void OnShutdown() { }
    }
}
```

### 事件订阅插件

```csharp
using ObsMCLauncher.Core.Plugins;

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

            // 用 EventNames 常量，不写魔法字符串
            context.SubscribeEvent(IPluginContext.EventNames.GameLaunched, OnGameLaunched);
            context.SubscribeEvent(IPluginContext.EventNames.GameClosed, OnGameClosed);

            context.LogMessage(PluginLogLevel.Debug, "[EventPlugin] Subscribed to events");
        }

        public void OnUnload()
        {
            _context?.LogMessage(PluginLogLevel.Debug, "[EventPlugin] Unloaded");
        }

        public void OnShutdown() { }

        private void OnGameLaunched(object? eventData)
        {
            _context?.LogMessage(PluginLogLevel.Debug, $"[EventPlugin] Game launched: {eventData}");
        }

        private void OnGameClosed(object? eventData)
        {
            _context?.LogMessage(PluginLogLevel.Debug, $"[EventPlugin] Game closed: {eventData}");
        }
    }
}
```

### 启动钩子插件（扩展 API 综合）

演示使用启动钩子在游戏启动前追加 JVM 参数、崩溃时异步上传报告：

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
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

            // 启动后展示当前账户信息
            context.RegisterGameLaunchHook("show-account",
                GameLaunchPhase.AfterLaunch, OnAfterLaunch);

            // 上传报告要走网络 → 用异步钩子，不要阻塞启动流程
            context.RegisterGameLaunchHookAsync("upload-report",
                GameLaunchPhase.OnCrash, OnCrashAsync);
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

        private async Task OnCrashAsync(GameLaunchHookContext ctx)
        {
            var context = _context;
            if (context == null) return;

            context.LogMessage(PluginLogLevel.Error, $"检测到崩溃，退出码 {ctx.ExitCode}");

            // ctx.CrashReport 是尽力查找的结果，可能还没落盘；
            // 一定要拿到报告文件时请改订阅 EventNames.CrashDetected。
            if (string.IsNullOrEmpty(ctx.CrashReport)) return;

            try
            {
                using var http = new HttpClient();
                var content = new MultipartFormDataContent
                {
                    { new StringContent(ctx.VersionId), "version" },
                    // 发往自己的服务前先脱敏
                    { new StringContent(context.SanitizeCrashReportText(ctx.CrashReport)), "report" }
                };
                await http.PostAsync("https://your-server.com/api/crash", content);
            }
            catch (Exception ex)
            {
                context.LogMessage(PluginLogLevel.Error, $"上传崩溃报告失败: {ex.Message}");
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

演示综合使用 `GetInstalledVersions` / `RegisterCommand` / `LogMessage`：

```csharp
using System;
using System.IO;
using System.IO.Compression;
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

### 崩溃分析插件（崩溃 API + UI 槽位）

崩溃后从弹窗里提供一个"交给我的服务分析"的入口：

```csharp
using Avalonia.Controls;
using ObsMCLauncher.Core.Plugins;

public class MyCrashPlugin : ILauncherPlugin
{
    private IPluginContext _context = null!;

    public string Id => "my.crashplugin";
    public string Name => "崩溃分析示例";
    public string Version => "1.0.0";
    public string Author => "You";
    public string Description => "把崩溃日志交给自己的服务分析";

    public void OnLoad(IPluginContext context)
    {
        _context = context;

        var button = new Button { Content = "用我的服务分析" };
        button.Click += async (_, _) =>
        {
            // 取当前上下文（用户正在看哪份报告）
            var path = context.GetActiveCrashContext()?.CrashReportPath;
            if (string.IsNullOrEmpty(path)) return;

            // 默认已脱敏，再手动过一遍更保险
            var text = context.SanitizeCrashReportText(
                context.ReadCrashReportText(path, maxChars: 100_000) ?? "");

            var result = await CallMyServiceAsync(text);   // 你自己的实现
            context.ShowNotification("分析结果", result, "info");
        };

        context.AddSlotContent("crash.dialog.actions", "analyze-btn", button);
    }

    public void OnUnload() => _context?.ClearSlotContent("crash.dialog.actions");
    public void OnShutdown() { }
}
```

---

## ⚠️ UI 框架说明

ObsMCLauncher 使用 **Avalonia UI** 框架开发，支持跨平台运行（Windows/macOS/Linux）。

插件开发时请注意：
- 使用 `Path.Combine()` 处理文件路径，确保跨平台兼容性
- UI 操作需在 UI 线程执行，用 `context.RunOnUiThread(...)`（不必自行引用 `Avalonia.Threading.Dispatcher`），参见 [3.5 任意 UI 访问](#35-任意-ui-访问)

---

## ❓ 常见问题

### Q: 插件可以访问哪些启动器功能？

A: 见 [API 总览](#api-总览)，按场景的说明在 [API 参考](#api-参考) 各章。

### Q: 插件如何拿到崩溃日志？

A: 两种时机——只想"知道游戏崩了"用 `GameLaunchPhase.OnCrash` 钩子（报告可能为 null）；需要**拿到报告文件**就订阅 `EventNames.CrashDetected`。详见 [7. 崩溃系统](#7-崩溃系统)。

### Q: 插件怎么把 UI 放进启动器已有页面？

A: 优先用 [3.4 UI 槽位](#34-ui-槽位)（生命周期由启动器管理）；槽位不够用再走 [3.5 任意 UI 访问](#35-任意-ui-访问)（自由但兼容性自负）。

### Q: 插件如何保存数据？

A: 用 `context.PluginDataDirectory` 下的自有文件，或直接用配置 API `GetConfig<T>` / `SaveConfig<T>`，见 [8. 日志与配置](#8-日志与配置)。

### Q: 插件可以添加新的 UI 页面吗？

A: 可以，用 `context.RegisterTab()` 在"更多"页面注册标签页，支持传入 Avalonia `Control` 作为自定义 UI，见 [3.1](#31-注册标签页)。

### Q: 插件可以拦截游戏启动吗？

A: 可以，注册 `GameLaunchPhase.BeforeLaunch` 钩子，在回调中设置 `ctx.CancelLaunch = true` 即可中止启动（后续 `BeforeLaunch` 钩子也会停止调用）。请谨慎使用，避免影响用户体验。

### Q: 插件可以获取用户的微软访问令牌吗？

A: **不能**。`GetCurrentAccount()` 仅返回 `Username` / `AccountType` / `UUID` / `IsDefault`，不含任何令牌字段。如需调用微软 API，请引导用户在插件内独立完成 OAuth 授权。

### Q: 插件下载文件会被沙箱限制吗？

A: `RequestDownload` 强制要求 `http://` 或 `https://` 协议、文件名禁含路径分隔符、目标目录需在启动器允许范围内。见 [6. 下载](#6-下载)。

### Q: 插件钩子触发顺序是怎样的？

A: 同一阶段按 `{pluginId}.{hookId}` 字典序触发，同步与异步钩子混排。`BeforeLaunch` 被取消后，后续 `BeforeLaunch` 钩子不再调用；其他阶段的所有钩子都会被调用。见 [5](#5-游戏启动生命周期钩子)。

### Q: 用户怎么临时禁用插件？

A: 在插件目录下放一个 `.disabled` 文件即可，见 [用户安装插件](#用户安装插件)。

### Q: 插件出错会导致启动器崩溃吗？

A: 不会，启动器会捕获插件异常并隔离错误，只会禁用有问题的插件。所有 API 调用（包括回调内部异常）都由启动器统一 try-catch，不会传播到调用方。

### Q: 如何调试插件？

A: 使用 Visual Studio 附加到 `ObsMCLauncher.Desktop` 进程进行调试；同时推荐用 `context.LogMessage(PluginLogLevel.Debug, ...)` 输出调试信息，可在启动器的"开发控制台"或日志文件中查看。

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
