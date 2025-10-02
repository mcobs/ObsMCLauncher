# ObsMCLauncher - 黑曜石MC启动器

<div align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/WPF-Windows-0078D6?style=for-the-badge&logo=windows" alt="WPF"/>
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="License"/>
</div>

<div align="center">
  <h3>🎮 一个现代化、美观且功能强大的 Minecraft 启动器</h3>
  <p>基于 WPF 和 Material Design 打造的全新 Minecraft 游戏管理体验</p>
</div>

---

## 📖 简介

**ObsMCLauncher（黑曜石MC启动器）** 是一款专为 Minecraft 玩家设计的现代化启动器，采用 Material Design 设计语言，提供直观易用的界面和强大的功能。无论你是休闲玩家还是资深玩家，ObsMCLauncher 都能为你提供流畅的游戏启动和管理体验。

### ✨ 主要特性

- 🎨 **现代化界面** - 采用 Material Design 设计，深色主题，美观大方
- 🚀 **快速启动** - 一键启动游戏，支持多版本管理
- 📦 **版本管理** - 支持下载和管理原版、Forge、Fabric、Quilt 等多种版本
- 🔧 **MOD 支持** - 轻松管理 MOD、材质包、光影、数据包和整合包
- ⚙️ **灵活配置** - 自定义 Java 路径、内存分配、JVM 参数等
- 👤 **多账户** - 支持微软账户和离线模式
- 🌍 **多语言** - 支持简体中文、繁体中文、英语、日语等
- 💾 **智能下载** - 多线程下载，支持多个下载源

---

## 🖼️ 界面预览

### 主页
- 快速启动游戏
- 版本选择
- 功能卡片导航
- 实时状态显示

### 版本下载
- 版本搜索和筛选
- 支持原版、Forge、Fabric 等加载器
- 版本状态管理（下载、已安装、启动）

### 资源中心
- MOD、材质包、光影、数据包、整合包
- 分类浏览和搜索
- 版本适配筛选
- 热门资源推荐

### 设置页面
- 游戏配置（路径、内存、Java）
- 启动器个性化设置
- 账户管理
- 下载源配置

---

## 🏗️ 项目结构

```
ObsMCLauncher/
│
├── App.xaml                          # 应用程序入口，全局资源定义
├── App.xaml.cs                       # 应用程序启动逻辑
├── MainWindow.xaml                   # 主窗口界面（导航框架）
├── MainWindow.xaml.cs                # 主窗口逻辑（页面导航）
├── ObsMCLauncher.csproj              # 项目配置文件
│
├── Pages/                            # 页面目录
│   ├── HomePage.xaml                 # 主页界面
│   ├── HomePage.xaml.cs              # 主页逻辑
│   ├── VersionDownloadPage.xaml      # 版本下载界面
│   ├── VersionDownloadPage.xaml.cs   # 版本下载逻辑
│   ├── ResourcesPage.xaml            # 资源下载界面
│   ├── ResourcesPage.xaml.cs         # 资源下载逻辑
│   ├── SettingsPage.xaml             # 设置界面
│   └── SettingsPage.xaml.cs          # 设置逻辑
│
├── Models/                           # 数据模型（待实现）
├── ViewModels/                       # 视图模型（待实现）
├── Services/                         # 服务层（待实现）
│   ├── GameService.cs                # 游戏启动服务
│   ├── DownloadService.cs            # 下载管理服务
│   └── AccountService.cs             # 账户管理服务
│
├── Utils/                            # 工具类（待实现）
└── Resources/                        # 资源文件（待实现）
```

### 架构说明

- **App.xaml** - 定义全局样式和 Material Design 主题
- **MainWindow** - 主窗口包含左侧导航栏和右侧内容区（Frame）
- **Pages** - 各个功能页面，采用 MVVM 架构模式
- **Services** - 业务逻辑层，处理游戏下载、启动、账户等核心功能
- **Models** - 数据模型层，定义实体类
- **ViewModels** - 视图模型层，连接界面和业务逻辑

---

## 🔧 构建方法

### 前置要求

- **操作系统**: Windows 10/11 (x64)
- **.NET SDK**: .NET 8.0 或更高版本
- **IDE** (可选): Visual Studio 2022 / JetBrains Rider / Visual Studio Code

### 安装 .NET SDK

如果还未安装 .NET SDK，请访问 [.NET 官网](https://dotnet.microsoft.com/download) 下载并安装。

验证安装：
```bash
dotnet --version
```

### 克隆项目

```bash
git clone https://github.com/yourusername/ObsMCLauncher.git
cd ObsMCLauncher
```

### 还原依赖

```bash
dotnet restore
```

### 构建项目

#### 开发构建（Debug）
```bash
dotnet build
```

#### 发布构建（Release）
```bash
dotnet build --configuration Release
```

### 运行项目

```bash
dotnet run
```

### 发布为独立应用程序

#### 发布为单个可执行文件（推荐）
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

#### 发布为框架依赖
```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

发布后的文件位于：`bin/Release/net8.0-windows/win-x64/publish/`

---

## 📦 依赖项

项目使用以下 NuGet 包：

- **MaterialDesignThemes** (4.9.0) - Material Design UI 组件库
- **MaterialDesignColors** (2.1.4) - Material Design 颜色主题

自动安装依赖：
```bash
dotnet restore
```

---

## 🚀 快速开始

1. **安装 .NET 8.0 SDK**
2. **克隆或下载项目**
3. **打开终端，进入项目目录**
4. **运行以下命令**：
   ```bash
   dotnet restore
   dotnet build
   dotnet run
   ```
5. **启动器将自动打开，开始使用！**

---

## 🛠️ 开发计划

### 当前状态：UI 框架完成 ✅

- [x] 主窗口和导航框架
- [x] 主页界面
- [x] 版本下载页面
- [x] 资源下载页面（MOD/材质/光影/数据包/整合包）
- [x] 设置页面

### 下一步计划

- [ ] 实现 MVVM 架构和数据绑定
- [ ] 游戏版本下载功能
- [ ] 游戏启动核心
- [ ] 账户系统（微软登录、离线模式）
- [ ] MOD 和资源包管理
- [ ] 下载进度显示
- [ ] 配置文件持久化
- [ ] 日志系统
- [ ] 自动更新功能
- [ ] 崩溃报告和错误处理

---

## 🤝 贡献指南

欢迎贡献代码、报告问题或提出建议！

### 如何贡献

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m '添加某个很棒的特性'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

### 代码规范

- 使用 C# 命名规范
- 保持代码整洁和注释
- 遵循 MVVM 架构模式
- 提交前测试代码

---

## 📄 许可证

本项目采用 **MIT 许可证** 开源。

```
MIT License

Copyright (c) 2025 ObsMCLauncher

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

详见 [LICENSE](LICENSE) 文件。

---

## 📞 联系方式

- **项目主页**: [GitHub Repository](https://github.com/yourusername/ObsMCLauncher)
- **问题反馈**: [Issues](https://github.com/yourusername/ObsMCLauncher/issues)
- **讨论区**: [Discussions](https://github.com/yourusername/ObsMCLauncher/discussions)

---

## ⚠️ 免责声明

本启动器为第三方工具，与 Mojang Studios 和 Microsoft 无关。Minecraft 是 Mojang Studios 的注册商标。使用本启动器需遵守 [Minecraft 最终用户许可协议](https://www.minecraft.net/zh-hans/eula)。

---

## 🌟 支持项目

如果你喜欢这个项目，请给我们一个 ⭐ Star！你的支持是我们最大的动力！

---

<div align="center">
  <p>使用 ❤️ 和 C# 构建</p>
  <p>© 2025 ObsMCLauncher. All rights reserved.</p>
</div>

