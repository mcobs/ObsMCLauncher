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
├── App.xaml                    # 全局样式和主题配置
├── MainWindow.xaml             # 主窗口（导航框架）
├── Pages/                      # 页面目录
│   ├── HomePage.xaml           # 主页
│   ├── AccountManagementPage.xaml  # 账号管理页面
│   ├── VersionDownloadPage.xaml    # 版本下载页面
│   ├── ResourcesPage.xaml      # 资源中心
│   └── SettingsPage.xaml       # 设置页面
├── Models/                     # 数据模型
├── Services/                   # 服务层（API、下载源管理）
└── Utils/                      # 工具类
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
