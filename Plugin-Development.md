# ObsMCLauncher 插件开发指南

欢迎来到 ObsMCLauncher 插件开发！本文档将指导您如何开发、测试和发布插件。

---

## 📖 目录

- [插件系统介绍](#插件系统介绍)
- [开发环境要求](#开发环境要求)
- [插件结构](#插件结构)
- [插件接口](#插件接口)
- [开发流程](#开发流程)
- [测试插件](#测试插件)
- [发布流程](#发布流程)
- [示例插件](#示例插件)
- [常见问题](#常见问题)

---

## 🔌 插件系统介绍

ObsMCLauncher 采用基于 .NET Assembly 的插件系统，支持开发者使用 C# 编写插件来扩展启动器功能。

### 插件特性

- ✅ 原生性能，无沙箱限制
- ✅ 完整访问启动器 API
- ✅ 支持 UI 扩展
- ✅ 事件驱动架构
- ✅ 配置文件支持

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

- **NuGet Package Explorer**（用于查看和编辑 NuGet 包）
- **ILSpy** 或 **dnSpy**（用于反编译和调试）

---

## 📦 插件结构

### 目录结构

```
YourPlugin/
├── YourPlugin.csproj          # 项目文件
├── Plugin.cs                  # 插件主类（实现 ILauncherPlugin）
├── plugin.json                # 插件元数据
├── icon.png                   # 插件图标（可选，128x128）
├── README.md                  # 插件说明
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
  "homepage": "https://github.com/yourusername/your-plugin",
  "repository": "https://github.com/yourusername/your-plugin",
  "updateUrl": "https://api.github.com/repos/yourusername/your-plugin/releases/latest",
  "minLauncherVersion": "1.0.0",
  "dependencies": [],
  "permissions": [
    "network",
    "filesystem"
  ]
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
| `homepage` | string | ⭕ | 插件主页 URL |
| `repository` | string | ⭕ | 源代码仓库 URL |
| `updateUrl` | string | ⭕ | 更新检查 API（GitHub Releases） |
| `minLauncherVersion` | string | ✅ | 最低启动器版本要求 |
| `dependencies` | array | ⭕ | 依赖的其他插件 ID 列表 |
| `permissions` | array | ⭕ | 请求的权限列表 |

---

## 🔧 插件接口

### ILauncherPlugin 接口

所有插件必须实现 `ILauncherPlugin` 接口：

```csharp
namespace ObsMCLauncher.Plugins
{
    public interface ILauncherPlugin
    {
        /// <summary>
        /// 插件唯一标识符
        /// </summary>
        string Id { get; }
        
        /// <summary>
        /// 插件显示名称
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 插件版本
        /// </summary>
        string Version { get; }
        
        /// <summary>
        /// 插件作者
        /// </summary>
        string Author { get; }
        
        /// <summary>
        /// 插件描述
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// 插件加载时调用
        /// </summary>
        /// <param name="context">插件上下文，提供启动器 API 访问</param>
        void OnLoad(IPluginContext context);
        
        /// <summary>
        /// 插件卸载时调用（可选实现）
        /// </summary>
        void OnUnload();
        
        /// <summary>
        /// 启动器关闭时调用（可选实现）
        /// </summary>
        void OnShutdown();
    }
}
```

### IPluginContext 接口

通过插件上下文访问启动器功能：

```csharp
namespace ObsMCLauncher.Plugins
{
    public interface IPluginContext
    {
        /// <summary>
        /// 获取启动器版本信息
        /// </summary>
        string LauncherVersion { get; }
        
        /// <summary>
        /// 获取插件数据目录（用于保存配置和数据）
        /// </summary>
        string PluginDataDirectory { get; }
        
        /// <summary>
        /// 通知管理器（显示通知）
        /// </summary>
        INotificationManager NotificationManager { get; }
        
        /// <summary>
        /// 对话框管理器（显示对话框）
        /// </summary>
        IDialogManager DialogManager { get; }
        
        /// <summary>
        /// 版本管理器（访问游戏版本）
        /// </summary>
        IVersionManager VersionManager { get; }
        
        /// <summary>
        /// 下载管理器（下载文件）
        /// </summary>
        IDownloadManager DownloadManager { get; }
        
        /// <summary>
        /// 注册菜单项（添加到"更多"菜单）
        /// </summary>
        /// <param name="name">菜单项名称</param>
        /// <param name="callback">点击回调</param>
        void RegisterMenuItem(string name, Action callback);
        
        /// <summary>
        /// 注册设置页面
        /// </summary>
        /// <param name="title">设置页面标题</param>
        /// <param name="page">WPF Page 对象</param>
        void RegisterSettingsPage(string title, object page);
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="handler">事件处理器</param>
        void SubscribeEvent(string eventName, Action<object> handler);
    }
}
```

### 可用事件

| 事件名称 | 触发时机 | 参数类型 |
|---------|---------|---------|
| `GameLaunching` | 游戏启动前 | `GameLaunchEventArgs` |
| `GameLaunched` | 游戏启动后 | `GameLaunchEventArgs` |
| `GameExited` | 游戏退出 | `GameExitEventArgs` |
| `VersionDownloaded` | 版本下载完成 | `VersionDownloadEventArgs` |
| `ThemeChanged` | 主题切换 | `ThemeChangedEventArgs` |

---

## 💻 开发流程

### 1. 创建项目

```bash
# 创建 Class Library 项目
dotnet new classlib -n YourPlugin -f net8.0-windows

cd YourPlugin

# 添加启动器引用（引用启动器的 exe 文件）
dotnet add reference path/to/ObsMCLauncher.exe
```

> **注意**：虽然启动器是单个 exe 文件，但它同样可以作为程序集被引用。确保在开发时将 `ObsMCLauncher.exe` 复制到可访问的位置。

### 2. 实现插件接口

创建 `Plugin.cs`：

```csharp
using ObsMCLauncher.Plugins;
using System;

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
            
            // 显示加载通知
            context.NotificationManager.ShowNotification(
                "插件加载",
                $"{Name} 已成功加载！",
                NotificationType.Success
            );
            
            // 注册菜单项
            context.RegisterMenuItem("Your Plugin Settings", OnSettingsClick);
            
            // 订阅事件
            context.SubscribeEvent("GameLaunching", OnGameLaunching);
        }
        
        public void OnUnload()
        {
            // 清理资源
        }
        
        public void OnShutdown()
        {
            // 保存配置等
        }
        
        private void OnSettingsClick()
        {
            _context?.NotificationManager.ShowNotification(
                Name,
                "打开设置页面...",
                NotificationType.Info
            );
        }
        
        private void OnGameLaunching(object args)
        {
            // 游戏启动前的处理
        }
    }
}
```

### 3. 创建 plugin.json

在项目根目录创建 `plugin.json`：


### 4. 编译插件

```bash
dotnet build -c Release
```

编译后的文件位于 `bin/Release/net8.0-windows/`

---

## 🧪 测试插件

### 本地测试

1. 在启动器安装目录找到 `plugins` 文件夹
2. 创建插件文件夹（与插件 ID 同名）：`plugins/your-plugin-id/`
3. 复制以下文件到插件文件夹：
   - `YourPlugin.dll`（插件主文件）
   - `plugin.json`（元数据）
   - 其他依赖的 DLL 文件
4. 重启启动器

### 文件夹结构示例

```
ObsMCLauncher/
└── plugins/
    └── your-plugin-id/
        ├── YourPlugin.dll
        ├── plugin.json
        └── icon.png（可选）
```

### 调试

使用 Visual Studio 附加到 `ObsMCLauncher.exe` 进程进行调试。

---

## 🚀 发布流程

### 1. 准备 GitHub 仓库

#### 仓库结构

```
your-plugin/
├── src/                    # 源代码
├── plugin.json             # 插件元数据
├── icon.png                # 插件图标
├── README.md               # 项目说明
├── LICENSE                 # 开源协议（推荐 MIT）
└── .github/
    └── workflows/
        └── release.yml     # 自动发布工作流（可选）
```

#### 添加 Topics

在 GitHub 仓库设置中添加以下 Topics：

```
obsmclauncher-plugin
obsmclauncher

```

### 2. 创建 Release

#### 手动发布

1. 编译插件（Release 模式）
2. 将以下文件打包成 ZIP：
   - `YourPlugin.dll`
   - `plugin.json`
   - `icon.png`（如有）
   - `README.md`
   - `LICENSE`
3. 在 GitHub 创建新 Release：
   - **Tag version**: `v1.0.0`（遵循 SemVer）
   - **Release title**: `YourPlugin v1.0.0`
   - **Description**: 更新日志
   - **Assets**: 上传 ZIP 文件


### 3. 提交到插件市场

#### 方式一：提交 PR 到插件索引仓库

1. Fork [ObsMCLauncher/PluginMarket](https://github.com/ObsMCLauncher/ObsMCLauncher-PluginMarket) 仓库
2. 在 `plugins/` 目录创建 `your-plugin-id.json`：

```json
{
  "id": "your-plugin-id",
  "name": "Your Plugin Name",
  "author": "Your Name",
  "description": "Plugin description",
  "version": "1.0.0",
  "category": "tools",
  "icon": "https://raw.githubusercontent.com/yourusername/your-plugin/main/icon.png",
  "repository": "https://github.com/yourusername/your-plugin",
  "releaseUrl": "https://api.github.com/repos/yourusername/your-plugin/releases/latest",
  "downloadUrl": "https://github.com/yourusername/your-plugin/releases/download/v1.0.0/YourPlugin.zip",
  "minLauncherVersion": "1.0.0",
  "tags": ["utility", "management"],
  "screenshots": [
    "https://raw.githubusercontent.com/yourusername/your-plugin/main/screenshots/1.png"
  ]
}
```

3. 提交 Pull Request

#### 方式二：GitHub Issue 提交

在 [PluginMarket Issues](https://github.com/ObsMCLauncher/PluginMarket/issues/new) 创建插件提交 Issue，包含：
- 插件名称和描述
- 仓库链接
- 最新 Release 链接
- 简要说明功能


## 📝 示例插件

### 简单通知插件

```csharp
using ObsMCLauncher.Plugins;

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
            context.NotificationManager.ShowNotification(
                "Hello",
                "Hello from plugin!",
                NotificationType.Info,
                3
            );
        }
        
        public void OnUnload() { }
        public void OnShutdown() { }
    }
}
```
```
---

## ❓ 常见问题

### Q: 插件可以访问哪些启动器功能？

A: 插件通过 `IPluginContext` 可以访问：
- 通知系统
- 对话框系统
- 版本管理
- 下载管理
- 事件订阅
- UI 扩展

### Q: 插件如何保存数据？

A: 使用 `context.PluginDataDirectory` 获取专属数据目录，在该目录下保存配置和数据文件。

### Q: 插件可以添加新的 UI 页面吗？

A: 可以，使用 `context.RegisterSettingsPage()` 注册设置页面，或通过菜单项打开自定义窗口。

### Q: 如何处理插件依赖？

A: 在 `plugin.json` 的 `dependencies` 字段声明依赖的其他插件 ID，启动器会自动检查和加载依赖。

### Q: 插件更新如何处理？

A: 启动器会通过 `updateUrl` 检查 GitHub Releases，发现新版本时提示用户更新。

### Q: 插件出错会导致启动器崩溃吗？

A: 不会，启动器会捕获插件异常并隔离错误，只会禁用有问题的插件。

---

## 参考资源

- [ObsMCLauncher 官方文档](https://github.com/mcobs/ObsMCLauncher)
- [.NET 8.0 文档](https://learn.microsoft.com/zh-cn/dotnet/)
- [WPF 开发指南](https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/)
- [Material Design in XAML](http://materialdesigninxaml.net/)

---

## 支持

- **GitHub Issues**: [提交问题](https://github.com/mcobs/ObsMCLauncher/issues)
- **讨论区**: [GitHub Discussions](https://github.com/mcobs/ObsMCLauncher/discussions)
- **黑曜石论坛**: [https://mcobs.cn/](https://mcobs.cn/)

---

## 许可证

本文档采用 [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) 许可协议。

插件开发者可以选择任何开源协议发布插件，推荐使用 MIT License。

---

**祝您开发愉快！**

如有任何问题，欢迎在 GitHub 上提问或参与讨论。

