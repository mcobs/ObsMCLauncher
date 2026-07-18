# ObsMCLauncher - 黑曜石MC启动器

<div align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/Avalonia-Cross%20Platform-1E9BDB?style=for-the-badge" alt="Avalonia"/>
  <img src="https://img.shields.io/badge/License-GPL--3.0-blue?style=for-the-badge" alt="License"/>
  <br/>
  <img src="https://github.com/x1aoren/ObsMCLauncher/actions/workflows/build.yml/badge.svg" alt="Build Status"/>
  <br/>
  <a href="https://deepwiki.com/mcobs/ObsMCLauncher"><img src="https://deepwiki.com/badge.svg" alt="Ask DeepWiki"></a>
</div>

<div align="center">
  <h3>🎮 一个现代化、美观的 Minecraft 启动器</h3>
</div>

---

## 📖 简介

**ObsMCLauncher** 是一款采用现代 UI 设计的 Minecraft 启动器，基于 Avalonia UI 框架开发，支持跨平台运行。

---

## 🏗️ 项目架构

```text
ObsMCLauncher/
├── ObsMCLauncher.Core/                  # 核心库（跨平台，无 UI 依赖）
│   ├── Bootstrap/                       # 启动引导
│   │   └── LauncherBootstrap.cs
│   ├── Models/                          # 数据模型
│   │   ├── LauncherConfig.cs            # 启动器配置
│   │   ├── GameAccount.cs               # 账号模型
│   │   ├── HomeCardInfo.cs              # 主页卡片模型
│   │   ├── VersionInitData.cs           # 版本级配置存储模型
│   │   └── ...
│   ├── Plugins/                         # 插件系统
│   │   ├── ILauncherPlugin.cs           # 插件接口
│   │   ├── IPluginContext.cs            # 插件上下文接口（含扩展 API）
│   │   ├── PluginContext.cs             # 插件上下文实现
│   │   ├── PluginApiModels.cs           # 扩展 API 模型（日志/版本/账户/钩子/下载）
│   │   ├── PluginLoader.cs              # 插件加载器
│   │   ├── PluginMarketService.cs       # 插件市场服务
│   │   ├── PluginMetadata.cs            # plugin.json 元数据
│   │   └── Events/                      # 插件事件参数
│   │       └── PluginEventArgs.cs
│   ├── Services/                        # 服务层
│   │   ├── Accounts/                    # 账号服务（微软/离线/Yggdrasil）
│   │   ├── Download/                    # 下载服务（多源/断点续传/SHA-1 校验）
│   │   ├── Installers/                  # 模组加载器安装（Forge/Fabric/NeoForge/Quilt/OptiFine）
│   │   ├── Minecraft/                   # Minecraft 核心服务
│   │   │   ├── MinecraftVersionService.cs
│   │   │   ├── LocalVersionService.cs
│   │   │   └── ModpackInstallService.cs
│   │   ├── Modrinth/                    # Modrinth 集成
│   │   ├── Mirror/                      # 镜像源服务
│   │   ├── ModConflictDetector.cs       # 模组冲突检测 + 版本范围解析
│   │   ├── NbtReader.cs                 # 轻量级 NBT 解析器（level.dat 版本提取）
│   │   ├── GameLauncher.cs              # 游戏启动服务
│   │   ├── JavaDetector.cs              # 跨平台 Java 检测
│   │   ├── VersionInitService.cs        # 版本级配置存储（OMCL/init.json）
│   │   ├── VersionConfigService.cs      # 版本隔离模式管理
│   │   ├── UpdateService.cs             # Velopack 增量更新
│   │   ├── GitHubProxyVelopackSource.cs # GitHub 代理更新源
│   │   └── ...
│   └── Utils/                           # 工具类
│       ├── DebugLogger.cs               # 统一日志
│       ├── SafeZipExtractor.cs          # ZIP Slip 防护
│       ├── FileHashVerifier.cs          # 文件哈希校验
│       ├── HttpClientFactory.cs         # 统一 HttpClient（SSL 验证可控）
│       └── ...
│
├── ObsMCLauncher.Desktop/               # Avalonia 桌面应用
│   ├── Assets/                          # 资源文件（图标/翻译/Logo）
│   ├── Converters/                      # XAML 转换器
│   ├── Services/                        # 平台服务（Dispatcher 等）
│   ├── Styles/                          # 样式主题
│   │   ├── Theme.axaml                  # 主题资源定义（颜色/画刷/阴影）
│   │   ├── Controls.axaml               # 控件基础样式
│   │   ├── AcrylicStyle.axaml           # 亚克力质感样式
│   │   ├── GlassStyle.axaml             # 磨砂玻璃质感样式
│   │   ├── FlatStyle.axaml              # 纯色扁平稳感样式
│   │   └── CardStyle.axaml              # 悬浮卡片质感样式
│   ├── ViewModels/                      # MVVM 视图模型
│   │   ├── MainWindowViewModel.cs
│   │   ├── HomeViewModel.cs             # 主页（含插件卡片管理）
│   │   ├── InstanceViewModel.cs         # 版本详情（含跨平台文件夹打开）
│   │   ├── MoreViewModel.cs             # 更多功能（含插件标签页管理）
│   │   ├── PluginTabViewModel.cs        # 插件自定义页面 ViewModel
│   │   └── ...
│   ├── Views/                           # 视图（AXAML）
│   └── Windows/                         # 独立窗口（崩溃/开发控制台）
│
├── tests/
│   └── ObsMCLauncher.Core.Tests/        # 单元测试 + 性能基准
│       ├── ModVersionRangeTests.cs      # 版本范围解析测试
│       ├── ModConflictDetectorTests.cs  # 冲突检测测试
│       ├── NbtReaderTests.cs            # NBT 解析测试
│       ├── PluginExtendedApiTests.cs    # 插件扩展 API 测试
│       ├── PluginCardFlowTests.cs       # 自建卡片全流程测试
│       ├── PluginPageFlowTests.cs       # 自定义页面全流程测试
│       ├── PluginCommandTests.cs        # 插件命令测试
│       ├── PluginEventTests.cs          # 插件事件测试
│       ├── PerformanceBenchmarkTests.cs # 性能基准测试
│       └── ...
│
└── Plugin-Development.md                # 插件开发指南
```

### 架构设计原则

- **Core/Desktop 分层**：`ObsMCLauncher.Core` 不依赖 Avalonia，可独立测试；`ObsMCLauncher.Desktop` 承载 UI 与平台交互
- **配置版本化**：每个版本的配置存储在自身目录的 `OMCL/init.json` 中，便于备份/迁移/删除
- **主题资源化**：颜色、画刷、阴影通过 `Theme.axaml` 集中定义，运行时通过 `DynamicResource` 绑定，质感切换不修改 `.axaml` 文件
- **插件隔离**：插件通过 `IPluginContext` 访问启动器能力，无法直接访问内部静态状态；命令/钩子/卡片均以 `{pluginId}.{id}` 形式命名防止冲突
- **安全边界**：所有 ZIP 解压经 `SafeZipExtractor` 防路径遍历；下载请求强制 `http/https` 协议；文件名禁止路径分隔符

---

## 🔌 插件系统 API

插件通过实现 `ILauncherPlugin` 接口并调用 `IPluginContext` 提供的 API 与启动器交互。

### 核心 API

| API | 说明 |
|-----|------|
| `RegisterTab` | 注册"更多"页面下的自定义标签页，支持传入 Avalonia UserControl 作为内容 |
| `RegisterHomeCard` | 注册主页卡片，可绑定自定义命令 |
| `RegisterCommand` | 注册命令，卡片点击时通过 `command:{pluginId}.{commandId}` 触发 |
| `SubscribeEvent` / `PublishEvent` | 订阅/发布全局事件（游戏启动、版本安装、账户变更、下载进度等） |
| `ShowNotification` / `UpdateNotification` / `CloseNotification` | 通知系统（info/success/warning/error/progress） |

### 扩展 API

| API | 说明 | 安全约束 |
|-----|------|----------|
| `LogMessage(level, message)` | 写入启动器统一日志，与启动器自身日志同源 | 消息为空时不调用 |
| `GetInstalledVersions()` | 获取已安装版本只读列表（VersionId/McVersion/LoaderType/Path/LastPlayed） | 异常时返回空列表 |
| `GetCurrentAccount()` | 获取当前账户精简信息（Id/Username/Type/UUID/IsDefault） | 不含任何令牌字段 |
| `RegisterGameLaunchHook(hookId, phase, handler)` | 注册启动生命周期钩子（BeforeLaunch/AfterLaunch/OnExited/OnCrash） | BeforeLaunch 可 `CancelLaunch` 中止启动；可追加 JVM/游戏参数 |
| `RequestDownload(request)` | 提交下载请求给启动器下载管理器统一调度 | 强制 http/https 协议；文件名禁含路径分隔符；返回任务 ID |

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
git clone https://github.com/x1aoren/ObsMCLauncher.git
cd ObsMCLauncher

# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行启动器
dotnet run --project ObsMCLauncher.Desktop
```

### 发布为可执行文件

```bash
# Windows
dotnet publish ObsMCLauncher.Desktop -c Release -r win-x64 -p:PublishSingleFile=true

# Linux
dotnet publish ObsMCLauncher.Desktop -c Release -r linux-x64 -p:PublishSingleFile=true

# macOS
dotnet publish ObsMCLauncher.Desktop -c Release -r osx-x64 -p:PublishSingleFile=true
```

### 运行测试

```bash
# 运行全部测试（313 个用例）
dotnet test tests/ObsMCLauncher.Core.Tests/ObsMCLauncher.Core.Tests.csproj

# 运行特定测试类
dotnet test tests/ObsMCLauncher.Core.Tests/ObsMCLauncher.Core.Tests.csproj --filter "FullyQualifiedName~PluginExtendedApiTests"
```

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
