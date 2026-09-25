# ObsMCLauncher - 黑曜石MC启动器

<div align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/Avalonia-Cross%20Platform-1E9BDB?style=for-the-badge" alt="Avalonia"/>
  <img src="https://img.shields.io/badge/License-GPL--3.0-blue?style=for-the-badge" alt="License"/>
  <br/>
  <img src="https://github.com/mcobs/ObsMCLauncher/actions/workflows/build.yml/badge.svg" alt="Build Status"/>
  <br/>
  <a href="https://deepwiki.com/mcobs/ObsMCLauncher"><img src="https://deepwiki.com/badge.svg" alt="Ask DeepWiki"></a>
</div>

<div align="center">
  <h3>🎮 一个现代化、美观的 Minecraft 启动器</h3>
</div>

---

## 📖 简介

**ObsMCLauncher**（黑曜石MC启动器）是一款基于 Avalonia UI 开发的跨平台 Minecraft 启动器，支持 Windows / Linux / macOS。

---

## 🏗️ 项目架构

```text
ObsMCLauncher/
├── ObsMCLauncher.Core/                  # 核心库（跨平台，无 UI 依赖）
│   ├── Bootstrap/                       # LauncherBootstrap 启动引导
│   ├── Models/                          # 配置、账号、主页卡片、崩溃报告、壁纸等模型
│   ├── Media/                           # 动态背景：格式探测、GIF/WebP 解码、帧调度
│   ├── Security/                        # 令牌加密与密钥存储（DPAPI / Keychain / libsecret）
│   ├── Plugins/                         # 插件加载器、上下文、市场、槽位注册、崩溃数据映射
│   ├── Services/                        # 服务层
│   │   ├── Accounts/                    # 微软 / Yggdrasil / 离线账号
│   │   ├── Crash/                       # 崩溃报告扫描、规则分析、文本脱敏
│   │   ├── Download/                    # 下载任务调度、多下载源、HTTP 下载
│   │   ├── Installers/                  # 模组加载器安装（Forge/Fabric/NeoForge/Quilt/OptiFine）
│   │   ├── Minecraft/                   # 版本获取、安装、本地版本、整合包安装
│   │   ├── Migration/                   # PCL / HMCL 数据迁移
│   │   ├── Modrinth/                    # Modrinth 集成
│   │   ├── Mirror/                      # 镜像源服务（MCIM 等）
│   │   ├── GameLauncher.cs              # 游戏启动与生命周期钩子
│   │   ├── JavaDetector.cs              # 跨平台 Java 检测
│   │   ├── LocalModScanner.cs           # 本地模组扫描（带持久化缓存）
│   │   ├── ModConflictDetector.cs       # 模组冲突检测 + 版本范围解析
│   │   ├── ModMetadataParser.cs         # 模组元数据解析
│   │   ├── ModTranslationService.cs     # 模组中文翻译
│   │   ├── NbtReader.cs                 # 轻量级 NBT 解析器（level.dat 版本提取）
│   │   ├── UpdateService.cs             # Velopack 增量更新
│   │   └── ...
│   └── Utils/                           # 日志、安全解压、哈希校验、统一 HttpClient 等
│
├── ObsMCLauncher.Desktop/               # Avalonia 桌面应用
│   ├── Assets/                          # 图标 / 侧边栏图标 / Logo
│   ├── Controls/                        # 自绘控件（标题栏、壁纸宿主、插件槽位宿主等）
│   ├── Converters/                      # XAML 转换器
│   ├── Services/                        # 外观应用、壁纸调度、窗口标题栏、崩溃提示等
│   ├── Styles/                          # Theme.axaml 主题资源、控件样式、欢迎向导样式
│   ├── ViewModels/                      # MVVM 视图模型（含首次启动向导页面）
│   ├── Views/                           # 页面与设置页
│   └── Windows/                         # 欢迎向导 / 崩溃窗口 / 开发控制台
│
├── tests/
│   └── ObsMCLauncher.Core.Tests/        # 单元测试（xunit，630 个用例）
│       ├── ModVersionRangeTests.cs      # 版本范围解析
│       ├── ModConflictDetectorTests.cs  # 冲突检测
│       ├── LocalModScannerTests.cs      # 本地模组扫描与缓存
│       ├── CrashReportAnalyzerTests.cs  # 崩溃报告分析
│       ├── WallpaperMediaTests.cs       # 壁纸格式探测与解码
│       ├── WallpaperRotationTests.cs    # 壁纸轮播
│       ├── PluginExtendedApiTests.cs    # 插件扩展 API
│       ├── PluginCrashApiTests.cs       # 插件崩溃数据 API
│       ├── PluginSlotRegistryTests.cs   # 插件 UI 槽位
│       ├── PluginCardFlowTests.cs       # 自建卡片全流程
│       ├── PluginPageFlowTests.cs       # 自定义页面全流程
│       ├── PerformanceBenchmarkTests.cs # 性能基准
│       └── ...
│
├── Plugin-Development.md                # 插件开发指南
└── FEATURES.md                          # 功能详解
```

### 架构设计原则

- **Core/Desktop 分层**：`ObsMCLauncher.Core` 不依赖 Avalonia，可独立测试；`ObsMCLauncher.Desktop` 承载 UI 与平台交互
- **配置版本化**：每个版本的配置存储在自身目录的 `OMCL/init.json` 中，便于备份/迁移/删除
- **主题资源化**：颜色、画刷、阴影通过 `Theme.axaml` 集中定义，运行时通过 `DynamicResource` 绑定，切换主题不修改 `.axaml` 文件
- **插件隔离**：插件通过 `IPluginContext` 访问启动器能力，无法直接访问内部静态状态；命令/钩子/卡片/槽位均以 `{pluginId}.{id}` 形式命名防止冲突
- **安全边界**：所有 ZIP 解压经 `SafeZipExtractor` 防路径遍历；下载请求强制 `http/https` 协议；文件名禁止路径分隔符；账户令牌经 `Core/Security` 加密落盘，插件侧只暴露不含令牌的账户信息
- **媒体与渲染分离**：壁纸的格式探测、解码与帧调度位于 `Core/Media`（仅依赖 SkiaSharp，不涉及 UI），实际绘制由 `Desktop` 的合成器线程完成

---

## 🔌 插件系统 API

插件通过实现 `ILauncherPlugin` 接口并调用 `IPluginContext` 提供的 API 与启动器交互。

### 核心 API

| API | 说明 |
|-----|------|
| `RegisterTab` / `RegisterTab(自定义 UI)` / `UnregisterTab` | 注册"更多"页面下的自定义标签页，支持传入 Avalonia 控件作为内容 |
| `RegisterHomeCard` / `UnregisterHomeCard` | 注册主页卡片，可指定默认尺寸档位，可绑定自定义命令 |
| `RegisterCommand` / `UnregisterCommand` | 注册命令，卡片点击时通过 `command:{pluginId}.{commandId}` 触发 |
| `SubscribeEvent` / `UnsubscribeEvent` / `PublishEvent` | 订阅/发布全局事件（GameLaunched、GameClosed、VersionSelected、CrashDetected、UiReady、下载进度等） |
| `ShowNotification` / `UpdateNotification` / `CloseNotification` | 通知系统（info/success/warning/error/progress） |

### 界面扩展 API

| API | 说明 |
|-----|------|
| `AddSlotContent` / `RemoveSlotContent` / `ClearSlotContent` | 向具名槽位注册自绘控件；槽位 id 为开放字符串，未开槽的 id 也会保留待宿主出现 |
| `GetSlotIds` / `GetSlotHost` | 查询可用槽位；取槽位宿主容器后可自行增删改其中的控件 |
| `GetUiRoot` / `TryFindControlByName` | 不受槽位限制地访问主窗口视觉树（页面结构变化时兼容风险由插件承担） |
| `IsUiReady` / `RunOnUiThread` | 判断主界面是否已就绪、把回调调度到 UI 线程 |

`GetUiRoot` 在 `OnLoad` 阶段必然返回 null——插件需等待 `UiReady` 事件（每个进程广播一次）或先判断 `IsUiReady`。

### 查询与配置 API

| API | 说明 |
|-----|------|
| `GetInstalledVersions` / `GetSelectedVersion` | 已安装版本只读列表 / 用户当前选中的版本 |
| `GetAccounts` / `GetCurrentAccount` | 账户列表 / 当前账户（均不含任何令牌字段） |
| `GetGameStatus` | 游戏进程状态（是否运行、版本、PID、启动时间） |
| `GetLaunchSettings` | 启动设置快照（内存、JVM 参数、Java 路径、游戏目录等） |
| `GetVersionRunDirectory` | 指定版本的运行目录（已套用版本隔离规则） |
| `GetMods` / `GetWorlds` / `GetResourcePacks` / `GetShaderPacks` | 选中版本的模组 / 存档（世界）/ 材质包 / 光影包列表 |
| `GetDownloadTasks` / `GetDownloadTaskStatus` | 全部下载任务快照 / 单个任务状态 |
| `GetConfig<T>` / `SaveConfig<T>` | 读写插件自身数据目录下的 `config.json` |
| `OpenUrl` / `NavigateTo` | 用系统浏览器打开链接 / 跳转到启动器内置页面 |
| `LogMessage` | 写入启动器统一日志，与启动器自身日志同源 |

### 崩溃数据 API

| API | 说明 |
|-----|------|
| `GetCrashReports` | 列出崩溃报告（只读快照，按时间倒序，可限定版本） |
| `AnalyzeCrashReport` | 用内置规则引擎分析报告（纯本地、无网络） |
| `ReadCrashReportText` | 读取报告原文，可截断，默认脱敏 |
| `SanitizeCrashReportText` | 手动脱敏（发送到外部服务前建议再过一遍） |
| `GetActiveCrashContext` | 取用户当前正在看的崩溃上下文（崩溃弹窗 / 崩溃分析页） |

### 启动生命周期与下载

| API | 说明 |
|-----|------|
| `RegisterGameLaunchHook`（含异步版本） / `UnregisterGameLaunchHook` | 注册启动钩子（BeforeLaunch / AfterLaunch / OnExited / OnCrash）；BeforeLaunch 可 `CancelLaunch` 中止启动，也可追加 JVM / 游戏参数 |
| `RequestDownload` | 提交下载请求交给启动器下载管理器统一调度，返回任务 ID |

### API 版本

`IPluginContext.ApiVersion` 与启动器版本号同步（去掉预发布后缀，如 `1.1.0-beta.1` → `1.1.0`）。语义：主版本递进可能包含破坏性变更，小版本只新增成员。插件在 `plugin.json` 中通过 `minPluginApiVersion` / `maxPluginApiVersion` 声明兼容范围。

详细 API 文档与示例见 [Plugin-Development.md](Plugin-Development.md)。

---

## 💻 支持的操作系统

| 平台 | 状态 | 架构 |
|:----:|:----:|:----:|
| Windows | ✅ 支持 | x86, x64, ARM64 |
| Linux | ✅ 支持 | x64, ARM64 |
| macOS | ✅ 支持 | x64, ARM64 |

---

## 运行要求

- Windows 10/11、Linux、macOS
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  - 如果运行时提示缺少 .NET，请下载并安装上述 Runtime

---

## 🔧 快速开始

### 前置要求

- Windows 10/11、Linux 或 macOS
- .NET 8.0 SDK

### 构建运行

```bash
# 克隆项目
git clone https://github.com/mcobs/ObsMCLauncher.git
cd ObsMCLauncher

# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行启动器
dotnet run --project ObsMCLauncher.Desktop
```

首次运行会进入启动向导，可在此完成主题、游戏目录、下载设置等配置，并选择是否从 PCL / HMCL 迁移数据。

### 发布为可执行文件

```bash
# Windows
dotnet publish ObsMCLauncher.Desktop -c Release -r win-x64

# Linux
dotnet publish ObsMCLauncher.Desktop -c Release -r linux-x64

# macOS
dotnet publish ObsMCLauncher.Desktop -c Release -r osx-x64
```

### 运行测试

```bash
# 运行全部测试（630 个用例）
dotnet test tests/ObsMCLauncher.Core.Tests/ObsMCLauncher.Core.Tests.csproj

# 运行特定测试类
dotnet test tests/ObsMCLauncher.Core.Tests/ObsMCLauncher.Core.Tests.csproj --filter "FullyQualifiedName~PluginExtendedApiTests"
```

---

## 📚 文档

| 文档 | 内容 |
|------|------|
| [Plugin-Development.md](Plugin-Development.md) | 插件开发指南（目录结构、事件、UI 扩展、崩溃数据等） |
| [.github/changelogs](.github/changelogs/CHANGELOG.md) | 版本更新日志 |

---

## 致谢

本项目的部分功能灵感来源于 [ClassIsland](https://github.com/ClassIsland/ClassIsland)（首次启动向导等），在此致谢。

WRC 牛逼。

---

## 📄 许可证

本项目采用 **GNU General Public License v3.0** 开源。

这意味着：
- ✅ 可以自由使用、修改和分发
- ✅ 必须开源修改后的代码
- ✅ 必须使用相同的 GPL-3.0 许可证
- ✅ 必须声明变更内容

详见 [LICENSE](LICENSE) 文件。

---

## ⚠️ 免责声明

本启动器为第三方工具，与 Mojang Studios 和 Microsoft 无关。Minecraft 是 Mojang Studios 的注册商标。

---

<div align="center">
  <p>© 2026 ObsMCLauncher</p>
</div>
