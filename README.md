# ObsMCLauncher - 黑曜石MC启动器

<div align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/Avalonia-Cross%20Platform-1E9BDB?style=for-the-badge" alt="Avalonia"/>
  <img src="https://img.shields.io/badge/License-GPL--3.0-blue?style=for-the-badge" alt="License"/>
  <br/>
  <img src="https://github.com/x1aoren/ObsMCLauncher/actions/workflows/build.yml/badge.svg" alt="Build Status"/>
</div>

<div align="center">
  <h3>🎮 一个现代化、美观的 Minecraft 启动器</h3>
</div>

---

## 📖 简介

**ObsMCLauncher** 是一款采用现代 UI 设计的 Minecraft 启动器，基于 Avalonia UI 框架开发，支持跨平台运行。

---

## 💻 支持的操作系统

| 平台 | 状态 | 架构 |
|:----:|:----:|:----:|
| Windows | ✅ 支持 | x86, x64, ARM64 |
| Linux | 🚧 计划中 | x64, ARM64 |
| macOS | 🚧 计划中 | x64, ARM64 |

---

## 运行要求

- Windows 10/11 (x64)
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  - 如果运行时提示缺少 .NET，请下载并安装上述 Runtime
  
---

## 🔧 快速开始

### 前置要求

- Windows 10/11 (x64)
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
dotnet publish ObsMCLauncher.Desktop -c Release -r win-x64 -p:PublishSingleFile=true
```

---

## 📦 项目结构

```text
ObsMCLauncher/
├── ObsMCLauncher.Core/                  # 核心库（跨平台）
│   ├── Bootstrap/                       # 启动引导
│   │   └── LauncherBootstrap.cs
│   ├── Models/                          # 数据模型
│   │   ├── LauncherConfig.cs            # 启动器配置模型
│   │   ├── GameAccount.cs               # 游戏账号模型
│   │   ├── ServerInfo.cs                # 服务器信息模型
│   │   ├── ScreenshotInfo.cs            # 截图信息模型
│   │   ├── HomeCardInfo.cs              # 主页卡片模型
│   │   ├── CurseForgeModels.cs          # CurseForge API模型
│   │   └── ...
│   ├── Plugins/                         # 插件系统
│   │   ├── PluginLoader.cs              # 插件加载器
│   │   ├── PluginMarketService.cs       # 插件市场服务
│   │   ├── PluginContext.cs             # 插件上下文实现
│   │   ├── ILauncherPlugin.cs           # 插件接口
│   │   └── PluginMetadata.cs            # plugin.json 元数据模型
│   ├── Services/                        # 服务层
│   │   ├── Accounts/                    # 账号服务
│   │   │   ├── AccountService.cs
│   │   │   ├── MicrosoftAuthService.cs  # 微软账号登录
│   │   │   └── LocalHttpServer.cs
│   │   ├── Download/                    # 下载服务
│   │   │   ├── DownloadTaskManager.cs
│   │   │   ├── DownloadSourceManager.cs
│   │   │   └── ...
│   │   ├── Installers/                  # 模组加载器安装
│   │   │   ├── ForgeService.cs
│   │   │   ├── FabricService.cs
│   │   │   ├── NeoForgeService.cs
│   │   │   ├── QuiltService.cs
│   │   │   └── OptiFineService.cs
│   │   ├── Minecraft/                   # Minecraft 相关服务
│   │   │   ├── MinecraftVersionService.cs
│   │   │   ├── LocalVersionService.cs
│   │   │   ├── ModpackInstallService.cs
│   │   │   └── ...
│   │   ├── Modrinth/                    # Modrinth 服务
│   │   ├── GameLauncher.cs              # 游戏启动服务
│   │   ├── UpdateService.cs             # 更新服务
│   │   └── ...
│   └── Utils/                           # 工具类
│       ├── VersionInfo.cs               # 版本信息
│       ├── GameVersionNumber.cs         # 游戏版本号解析
│       └── ...
│
├── ObsMCLauncher.Desktop/               # Avalonia 桌面应用
│   ├── Assets/                          # 资源文件
│   │   ├── LoaderIcons/                 # 模组加载器图标
│   │   ├── logo.png                     # 启动器 Logo
│   │   └── mod_translations.txt         # MOD中文翻译数据
│   ├── Converters/                      # XAML 转换器
│   │   ├── NotConverter.cs
│   │   ├── NullToBoolConverter.cs
│   │   ├── BitmapAssetValueConverter.cs
│   │   └── ...
│   ├── Styles/                          # 样式主题
│   │   ├── Controls.axaml               # 控件样式
│   │   └── Theme.axaml                  # 主题配置
│   ├── ViewModels/                      # MVVM 视图模型
│   │   ├── MainWindowViewModel.cs       # 主窗口 ViewModel
│   │   ├── HomeViewModel.cs             # 主页 ViewModel
│   │   ├── SettingsViewModel.cs         # 设置 ViewModel
│   │   ├── PluginsViewModel.cs          # 插件 ViewModel
│   │   ├── Dialogs/                     # 对话框服务
│   │   ├── Notifications/               # 通知服务
│   │   └── ...
│   ├── Views/                           # 视图（AXAML）
│   │   ├── MainWindow.axaml             # 主窗口
│   │   ├── HomeView.axaml               # 主页
│   │   ├── SettingsView.axaml           # 设置页面
│   │   ├── MoreView.axaml               # 更多功能页面
│   │   └── ...
│   ├── Windows/                         # 独立窗口
│   │   ├── CrashWindow.axaml            # 崩溃报告窗口
│   │   └── DevConsoleWindow.axaml       # 开发者控制台
│   ├── App.axaml                        # 应用程序资源
│   ├── App.axaml.cs                     # 应用程序入口
│   └── Program.cs                       # 程序入口
│
└── Plugin-Development.md                 # 插件开发指南
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
