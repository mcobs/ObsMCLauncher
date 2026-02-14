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
    }
}
```

---

## 🔧 API 参考

### 1. 事件系统

订阅和发布事件：

```csharp
public void OnLoad(IPluginContext context)
{
    context.SubscribeEvent("GameLaunched", OnGameLaunched);
    context.SubscribeEvent("GameClosed", OnGameClosed);
}

private void OnGameLaunched(object? eventData)
{
    System.Diagnostics.Debug.WriteLine("游戏已启动");
}

private void OnGameClosed(object? eventData)
{
    System.Diagnostics.Debug.WriteLine("游戏已关闭");
}
```

**可用事件**：
- `GameLaunched` - 游戏启动
- `GameClosed` - 游戏关闭
- `VersionDownloaded` - 版本下载完成
- 插件也可以发布自定义事件

### 2. 注册UI标签页

插件可以在"更多"页面添加自己的标签页：

```csharp
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

**说明**：
- 标签页会显示在"更多"页面的顶部导航栏
- `tabId` 必须唯一，建议使用插件ID作为前缀
- 图标使用 Material Design 图标名称

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

### Q: 插件如何保存数据？

A: 使用 `context.PluginDataDirectory` 获取专属数据目录，在该目录下保存配置和数据文件。

### Q: 插件可以添加新的 UI 页面吗？

A: 可以，使用 `context.RegisterTab()` 在"更多"页面注册标签页。

### Q: 插件出错会导致启动器崩溃吗？

A: 不会，启动器会捕获插件异常并隔离错误，只会禁用有问题的插件。

### Q: 如何调试插件？

A: 使用 Visual Studio 附加到 `ObsMCLauncher.Desktop` 进程进行调试。

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
