# ObsMCLauncher - 黑曜石MC启动器

<div align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/WPF-Windows-0078D6?style=for-the-badge&logo=windows" alt="WPF"/>
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="License"/>
  <br/>
  <img src="https://github.com/x1aoren/ObsMCLauncher/actions/workflows/build.yml/badge.svg" alt="Build Status"/>
</div>

<div align="center">
  <h3>🎮 一个现代化、美观的 Minecraft 启动器</h3>
</div>

---

## 📖 简介

**ObsMCLauncher** 是一款采用现代 UI 设计的 Minecraft 启动器，提供流畅的游戏管理体验。

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
dotnet run
```

### 发布为可执行文件

```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
```

---

## 📦 项目结构

```text
ObsMCLauncher/
├── App.xaml                         # 全局样式和主题配置
├── MainWindow.xaml                  # 主窗口（导航框架）
├── Assets/                          # 资源文件
│   ├── LoaderIcons/                 # 模组加载器图标
│   └── mod_translations.txt         # MOD中文翻译数据
├── Pages/                           # 页面目录
│   ├── HomePage.xaml                # 主页（版本列表）
│   ├── AccountManagementPage.xaml   # 账号管理页面
│   ├── VersionDownloadPage.xaml     # 版本下载页面
│   ├── VersionDetailPage.xaml       # 版本详情页（安装Forge/Fabric等）
│   ├── ResourcesPage.xaml           # 资源中心（MOD/材质包等）
│   ├── ModDetailPage.xaml           # 资源详情页
│   ├── MorePage.xaml                # 更多功能页面
│   └── SettingsPage.xaml            # 设置页面
├── Models/                          # 数据模型
│   ├── CurseForgeModels.cs          # CurseForge API模型
│   ├── ModrinthModels.cs            # Modrinth API模型
│   ├── GameAccount.cs               # 游戏账号模型
│   ├── LauncherConfig.cs            # 启动器配置模型
│   ├── GameDirectoryType.cs         # 版本隔离类型
│   └── ModTranslation.cs            # MOD翻译模型
├── Services/                        # 服务层
│   ├── MinecraftVersionService.cs   # Minecraft版本管理
│   ├── DownloadService.cs           # 文件下载服务
│   ├── DownloadSourceManager.cs     # 下载源管理
│   ├── DownloadTaskManager.cs       # 下载任务管理
│   ├── GameLauncher.cs              # 游戏启动服务
│   ├── ForgeService.cs              # Forge服务
│   ├── NeoForgeService.cs           # NeoForge服务
│   ├── FabricService.cs             # Fabric服务
│   ├── OptiFineService.cs           # OptiFine服务
│   ├── QuiltService.cs              # Quilt服务
│   ├── CurseForgeService.cs         # CurseForge API服务
│   ├── ModrinthService.cs           # Modrinth API服务
│   ├── ModpackInstallService.cs     # 整合包安装服务
│   ├── ModTranslationService.cs     # MOD翻译服务
│   ├── LocalVersionService.cs       # 本地版本服务
│   ├── MicrosoftAuthService.cs      # 微软账号登录
│   └── AccountService.cs            # 账号管理服务
└── Utils/                           # 工具类
    ├── DialogManager.cs             # 对话框管理器
    ├── NotificationManager.cs       # 通知管理器
    ├── SystemInfo.cs                # 系统信息
    ├── VersionInfo.cs               # 版本信息
    ├── GameVersionNumber.cs         # 游戏版本号解析
    └── ApiTester.cs                 # API测试工具
```

---

## 📄 许可证

本项目采用 **MIT 许可证** 开源。详见 [LICENSE](LICENSE) 文件。

---

## ⚠️ 免责声明

本启动器为第三方工具，与 Mojang Studios 和 Microsoft 无关。Minecraft 是 Mojang Studios 的注册商标。

---

<div align="center">
  <p>使用 ❤️ 和 C# 构建</p>
  <p>© 2025 ObsMCLauncher</p>
</div>
