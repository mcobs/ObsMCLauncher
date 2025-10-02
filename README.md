# ObsMCLauncher - 黑曜石MC启动器

<div align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/WPF-Windows-0078D6?style=for-the-badge&logo=windows" alt="WPF"/>
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="License"/>
</div>

<div align="center">
  <h3>🎮 一个现代化、美观的 Minecraft 启动器</h3>
</div>

---

## 📖 简介

**ObsMCLauncher** 是一款采用现代 UI 设计的 Minecraft 启动器，提供流畅的游戏管理体验。

---

## 🎨 UI 技术栈

### 核心框架
- **.NET 8.0** - 最新的 .NET 框架
- **WPF (Windows Presentation Foundation)** - Windows 桌面应用框架
- **XAML** - 界面标记语言

### UI 设计
- **Material Design** - 采用 Google Material Design 设计规范
  - `MaterialDesignThemes` (4.9.0) - UI 组件库
  - `MaterialDesignColors` (2.1.4) - 颜色主题
  
### 设计风格
- **WinUI 3 风格** - 参考 Windows 11 设计语言
  - 纯正暗色模式（#202020 背景）
  - 翡翠绿主题色（#10B981）
  - 12px 圆角卡片设计
  - 柔和阴影和发光效果
  - 流畅的页面过渡动画

### 界面特点
- 🎨 现代化深色主题
- ✨ 流畅的动画过渡
- 🎴 卡片式布局设计
- 💚 翡翠绿配色方案
- 🌊 呼吸动画和悬停效果

---

## 👤 账号管理功能

### 功能概述
启动器支持多账号管理，方便切换不同的游戏身份。

### 账号类型

#### 1. 微软账户 (Microsoft Account)
- ✅ 支持正版 Minecraft 账户登录
- ✅ 显示正版皮肤和披风
- ✅ 可进入正版验证服务器
- ⚠️ 需要购买正版游戏

#### 2. 离线账户 (Offline Account)
- ✅ 无需购买即可使用
- ✅ 自定义任意用户名
- ✅ 适合单机和离线服务器
- ⚠️ 无法进入正版验证服务器

### 账号管理界面
- 📋 账号列表展示
- ⭐ 设置默认账号
- ✏️ 编辑账号信息
- 🗑️ 删除不需要的账号
- 🔄 刷新微软账户状态

### 快速切换
在主页可以快速选择当前使用的账号，无需进入设置页面。

---

## 🔧 快速开始

### 前置要求
- Windows 10/11 (x64)
- .NET 8.0 SDK

### 构建运行
```bash
# 克隆项目
git clone https://github.com/yourusername/ObsMCLauncher.git
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
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## 📦 项目结构

```
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
