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

## 📁 插件目录位置

### 插件安装目录

ObsMCLauncher 支持三种插件目录位置（可在设置中配置）：

1. **运行目录\OMCL\plugins\** （默认，便携模式）
   ```
   启动器目录\OMCL\plugins\
   ```
   示例：`C:\Program Files\ObsMCLauncher\OMCL\plugins\`

2. **%APPDATA%\ObsMCLauncher\plugins\** （系统目录）
   ```
   C:\Users\[用户名]\AppData\Roaming\ObsMCLauncher\plugins\
   ```

3. **自定义目录** （用户指定的任意位置）

**默认使用运行目录，适合便携使用。** 用户可以在"设置 - 文件存储设置 - 插件目录位置"中更改。

### 目录结构

```
ObsMCLauncher/                           # 启动器运行目录
├── ObsMCLauncher.exe
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
├── OMCL.ini                             # 引导配置文件
└── (其他启动器文件)
```

**说明**：
- 插件的所有文件（程序、配置、数据）都在同一个目录下
- 插件可以在自己的目录下创建任何配置文件或子文件夹
- `PluginDataDirectory` 返回的就是插件自己的目录

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
        /// 通知管理器
        /// </summary>
        NotificationManager NotificationManager { get; }
        
        /// <summary>
        /// 对话框管理器
        /// </summary>
        DialogManager DialogManager { get; }
        
        /// <summary>
        /// 注册插件标签页（显示在"更多"页面）
        /// </summary>
        /// <param name="title">标签页标题</param>
        /// <param name="content">标签页内容（WPF Page或UserControl）</param>
        /// <param name="icon">图标名称（MaterialDesign图标，可选）</param>
        void RegisterTab(string title, object content, string? icon = null);
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="handler">事件处理器</param>
        void SubscribeEvent(string eventName, Action<object> handler);
        
        /// <summary>
        /// 发布事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="eventData">事件数据</param>
        void PublishEvent(string eventName, object eventData);
    }
}
```

---

## 🔧 API 参考

### 1. 通知系统

显示桌面通知：

```csharp
// 显示通知
context.NotificationManager.ShowNotification(
    "标题",
    "消息内容",
    NotificationType.Info,
    5  // 显示5秒，可选参数
);
```

**通知类型**：
- `NotificationType.Info` - 信息（蓝色）
- `NotificationType.Success` - 成功（绿色）
- `NotificationType.Warning` - 警告（黄色）
- `NotificationType.Error` - 错误（红色）

### 2. 对话框系统

显示各种对话框：

```csharp
// 确认对话框
bool confirmed = await context.DialogManager.ShowConfirmDialogAsync(
    "确认操作",
    "确定要继续吗？",
    "确定",
    "取消"
);

if (confirmed)
{
    // 用户点击了确定
}

// 输入对话框
string? input = await context.DialogManager.ShowInputDialogAsync(
    "输入",
    "请输入您的名称：",
    "默认值"
);

if (!string.IsNullOrEmpty(input))
{
    // 用户输入了内容
}
```

### 3. 事件系统

订阅和发布事件：

```csharp
// 订阅事件
context.SubscribeEvent("GameLaunched", OnGameLaunched);

private void OnGameLaunched(object eventData)
{
    // 处理游戏启动事件
    Debug.WriteLine("游戏已启动");
}

// 发布自定义事件
context.PublishEvent("MyCustomEvent", new { Data = "Hello" });
```

**可用事件**：
- `GameLaunched` - 游戏启动
- `GameClosed` - 游戏关闭
- `VersionDownloaded` - 版本下载完成
- 插件也可以发布自定义事件

### 4. 注册UI标签页

插件可以在"更多"页面添加自己的UI：

```csharp
public void OnLoad(IPluginContext context)
{
    // 创建自定义页面（WPF Page 或 UserControl）
    var myPage = new MyPluginPage();
    
    // 注册标签页
    context.RegisterTab(
        "我的插件",           // 标签页标题
        myPage,               // 页面内容
        "Star"                // MaterialDesign图标名称（可选）
    );
}
```

**图标示例**：
- `"Star"` - 星星图标
- `"Settings"` - 设置图标
- `"Heart"` - 心形图标
- `"Puzzle"` - 拼图图标
- 更多图标参考：[Material Design Icons](https://pictogrammers.com/library/mdi/)

### 5. 插件数据目录

插件数据目录就是插件自己的安装目录：

```csharp
// 获取插件数据目录（返回插件自己的目录路径）
string dataDir = context.PluginDataDirectory;
// 返回：运行目录/plugins/my-plugin/
// 示例：C:\Program Files\ObsMCLauncher\plugins\my-plugin\

// 保存配置文件到插件目录（直接创建文件，无需手动创建目录）
var configPath = Path.Combine(dataDir, "config.json");
File.WriteAllText(configPath, jsonConfig);

// 读取配置文件
if (File.Exists(configPath))
{
    var config = File.ReadAllText(configPath);
}

// 如需子文件夹，自行创建
var dataFolder = Path.Combine(dataDir, "data");
if (!Directory.Exists(dataFolder))
{
    Directory.CreateDirectory(dataFolder);
}

// 保存数据文件
var dataFile = Path.Combine(dataFolder, "cache.db");
// ... 保存数据
```

**说明**：
- `PluginDataDirectory` 返回插件自己的安装目录路径
- 不会自动创建任何子目录，由开发者根据需要自行创建
- 插件的所有文件（DLL、配置、数据）都在同一个目录下
- 便于备份和迁移（复制整个插件文件夹即可）

### 6. 启动器版本信息

获取启动器版本：

```csharp
string version = context.LauncherVersion;
// 示例：1.0.0

// 可用于版本兼容性检查
if (version.StartsWith("1."))
{
    // 适用于 1.x 版本
}
```

---

## 💻 开发流程

### 1. 创建项目

```bash
# 创建 Class Library 项目
dotnet new classlib -n YourPlugin -f net8.0-windows

cd YourPlugin

# 添加启动器引用
dotnet add reference path/to/ObsMCLauncher.dll
```

> **注意**：虽然启动器是单个 exe 文件，但它同样可以作为程序集被引用。

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
            // 保存配置到插件目录
            var configPath = Path.Combine(_context.PluginDataDirectory, "config.json");
            File.WriteAllText(configPath, "{}");
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

1. **找到启动器运行目录**
   - 开发环境：`H:\projects\ObsMCLauncher\bin\Debug\net8.0-windows\`
   - 发布后：启动器的安装目录

2. **创建 plugins 文件夹**（如果不存在）
   ```
   启动器目录/plugins/
   ```

3. **创建插件文件夹**（与插件 ID 同名）
   ```
   启动器目录/plugins/your-plugin-id/
   ```

4. **复制插件文件**
   - `YourPlugin.dll`（插件主文件）
   - `plugin.json`（元数据）
   - `icon.png`（可选）
   - 其他依赖的 DLL 文件

5. **重启启动器**

### 文件夹结构示例

```
ObsMCLauncher/
├── ObsMCLauncher.exe          # 启动器主程序
└── plugins/                    # 插件目录
    └── your-plugin-id/         # 插件文件夹
        ├── YourPlugin.dll      # 插件程序集
        ├── plugin.json         # 元数据
        └── icon.png            # 图标（可选）
```
### 调试

使用 Visual Studio 附加到 `ObsMCLauncher.exe` 进程进行调试。

---

### 安装示例

假设启动器安装在 `C:\Program Files\ObsMCLauncher\`，下载了 `hello-plugin.zip`：

```
hello-plugin.zip
├── hello-plugin.dll
├── plugin.json
└── icon.png
```

解压到：

```
C:\Program Files\ObsMCLauncher\plugins\hello-plugin\
├── hello-plugin.dll
├── plugin.json
└── icon.png
```

最终目录结构：

```
C:\Program Files\ObsMCLauncher\
├── ObsMCLauncher.exe        # 启动器主程序
├── plugins\                  # 插件目录
│   └── hello-plugin\         # 插件文件夹
│       ├── hello-plugin.dll
│       ├── plugin.json
│       └── icon.png
└── (其他启动器文件)
```

### 查看已安装的插件

1. 启动 ObsMCLauncher
2. 点击左侧导航栏的"更多"
3. 点击顶部的"插件"标签
4. 查看插件列表，包含：
   - 插件图标
   - 插件名称、版本、作者
   - 加载状态（已加载/加载失败）
   - 插件描述

### 插件UI

如果插件注册了自定义标签页，会在"更多"页面的导航栏显示，点击可查看插件的UI界面。

### 卸载插件

1. 关闭启动器
2. 打开启动器安装目录下的 `plugins` 文件夹
3. 删除对应的插件文件夹
4. 重启启动器

**注意**：删除插件文件夹会完全删除插件的所有文件（程序、配置、数据），请在删除前备份重要数据。

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

1. **编译插件**（Release 模式）
   ```bash
   dotnet build -c Release
   ```

2. **打包成 ZIP**（⚠️ 注意：文件直接放在ZIP根目录）
   
   进入编译输出目录：
   ```bash
   cd bin/Release/net8.0-windows/
   ```
   
   选择需要的文件并打包（**不要包含文件夹**）：
   ```bash
   # Windows PowerShell
   Compress-Archive -Path YourPlugin.dll,plugin.json,icon.png -DestinationPath YourPlugin.zip
   
   # 或使用 7-Zip 等工具，确保文件在ZIP根目录
   ```
   
   打包的文件应包括：
   - `YourPlugin.dll` - 插件主程序集
   - `plugin.json` - 元数据文件
   - `icon.png` - 图标（可选）
   - 其他依赖的 DLL 文件（如有）
   - `README.md`（可选）
   - `LICENSE`（可选）
   
   ⚠️ **重要**：确保文件直接在ZIP根目录，而不是嵌套在文件夹中！

3. **在 GitHub 创建新 Release**：
   - **Tag version**: `v1.0.0`（遵循 SemVer）
   - **Release title**: `YourPlugin v1.0.0`
   - **Description**: 更新日志
   - **Assets**: 上传 ZIP 文件


### 3. 提交到插件市场

#### 插件市场索引格式

插件市场使用JSON格式的索引文件，包含所有可用插件的信息：

```json
{
  "version": "1.0",
  "lastUpdate": "2025-11-01T00:00:00Z",
  "plugins": [
    {
      "id": "your-plugin-id",
      "name": "Your Plugin Name",
      "author": "Your Name",
      "description": "Plugin description",
      "version": "1.0.0",
      "category": "工具",
      "icon": "https://raw.githubusercontent.com/yourusername/your-plugin/main/icon.png",
      "repository": "https://github.com/yourusername/your-plugin",
      "releaseUrl": "https://api.github.com/repos/yourusername/your-plugin/releases/latest",
      "downloadUrl": "https://github.com/yourusername/your-plugin/releases/download/v1.0.0/your-plugin.zip",
      "minLauncherVersion": "1.0.0",
      "tags": ["工具", "实用"],
      "downloads": 0,
      "rating": 0
    }
  ]
}
```

**字段说明**：
- `id`: 插件唯一标识符（与文件夹名一致）
- `name`: 插件显示名称
- `author`: 作者名称
- `description`: 插件描述
- `version`: 当前版本号
- `category`: 分类ID（使用英文ID，参考下方分类表）
- `icon`: 图标URL（建议使用GitHub raw链接或其他CDN）
- `repository`: 源代码仓库URL
- `releaseUrl`: GitHub Release API URL（可选）
- `downloadUrl`: **插件ZIP包的直接下载链接**（必需）
- `minLauncherVersion`: 最低启动器版本要求
- `tags`: 标签数组
- `downloads`: 下载次数（由市场维护）
- `rating`: 评分（0-5）

**可用分类** (从云端动态获取: [categories.json](https://raw.githubusercontent.com/mcobs/ObsMCLauncher-PluginMarket/refs/heads/main/categories.json))：
- `utility` - 实用工具
- `appearance` - 外观美化
- `management` - 管理工具
- `integration` - 服务集成
- `enhancement` - 功能增强

**重要**：`downloadUrl` 必须是可直接下载的ZIP文件链接。

**ZIP包结构要求**（⚠️ 重要）：

✅ **正确的ZIP包结构**（文件直接放在ZIP根目录）：
```
your-plugin.zip
├── your-plugin.dll          # 插件主程序集
├── plugin.json              # 插件元数据
├── icon.png                 # 插件图标（可选）
├── DependencyLib.dll        # 依赖库（如有）
└── README.md                # 说明文档（可选）
```

❌ **错误的ZIP包结构**（不要在ZIP内创建文件夹）：
```
your-plugin.zip
└── your-plugin/             ❌ 不要这样！启动器会自动创建插件目录
    ├── your-plugin.dll
    ├── plugin.json
    └── icon.png
```

**说明**：启动器会将ZIP包直接解压到 `plugins/your-plugin-id/` 目录，因此ZIP包内的文件必须直接放在根目录，不要额外嵌套文件夹。

#### 方式一：提交 PR 到插件索引仓库

1. Fork [ObsMCLauncher-PluginMarket](https://github.com/mcobs/ObsMCLauncher-PluginMarket) 仓库
2. 编辑 `plugins.json` 文件，在 `plugins` 数组中添加您的插件信息
3. 提交 Pull Request，标题格式：`[新增插件] 插件名称 v版本号`
4. 等待审核通过

#### 方式二：GitHub Issue 提交

在 [PluginMarket Issues](https://github.com/mcobs/ObsMCLauncher-PluginMarket/issues/new) 创建插件提交 Issue，模板：

```
插件名称：您的插件名称
插件ID：your-plugin-id
版本：1.0.0
作者：您的名字
分类：工具/界面/游戏增强/实用程序
描述：插件的简要描述

仓库链接：https://github.com/yourusername/your-plugin
Release链接：https://github.com/yourusername/your-plugin/releases/tag/v1.0.0
下载链接：https://github.com/yourusername/your-plugin/releases/download/v1.0.0/your-plugin.zip

功能说明：
- 功能1
- 功能2
- 功能3
```


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

插件开发者可以选择任何开源协议发布插件，推荐使用 MIT License 或 GPL-3.0。

**注意**：ObsMCLauncher 本身采用 GPL-3.0 许可证，如果您的插件与启动器深度集成，可能需要考虑使用兼容的许可证。

---

**祝您开发愉快！**

如有任何问题，欢迎在 GitHub 上提问或参与讨论。

