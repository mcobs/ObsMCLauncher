## [v1.0.0] - 2026-05-xx

### 新增
- 适配Linux和macOS平台，支持多平台构建和运行
- GitHub CI构建支持Linux和macOS，Release自动发布多平台版本
- 更新系统支持多平台资产匹配

### 优化
- Java检测适配Linux/macOS，支持各平台常见Java安装路径
- 游戏目录默认路径适配各平台（Windows: %APPDATA%, macOS: ~/Library/Application Support, Linux: ~/.minecraft）
- 打开文件夹功能跨平台适配（Windows: explorer, macOS: open, Linux: xdg-open）
- Natives库提取和规则判断适配当前操作系统
- 路径分隔符使用Path.DirectorySeparatorChar替代硬编码反斜杠

### 修复
- Windows 版本运行时不再出现控制台窗口（OutputType 条件设为 WinExe）
- CI 构建添加 NuGet 包缓存，加速重复构建
- CurseForge 搜索接口接入双层缓存，减少重复 API 请求
- 修复 Ping 握手包协议版本字段错误导致无法连接服务器
- 修复 Ping 超时时 Task.WhenAny 导致未观察异常引发崩溃
- 修复 BoolToColorConverter/PingLevelToColorConverter 返回字符串导致 Color 绑定失败
- 修复 ParseMotd 在 text 字段为空时未回退到 extra 数组，导致部分服务器 MOTD 无法解析
- 修复 ParseMotd 遍历 extra 数组时遇到纯字符串元素崩溃
- 修复 RefreshStatusAsync 数据写入 config 对象后因 JsonIgnore 未持久化，FilterAsync 重载丢失所有运行时数据
- 新增服务器图标显示（从协议获取 base64 图标并缓存到本地）
- 新增 FilePathToBitmapConverter 将图标文件路径转换为 Bitmap 供 Image.Source 绑定
- 修复 Version 为空时显示"版本: "，改为显示"版本: 未知"

### 服务器管理
- Ping 检测升级为真实 Minecraft 协议，返回真实延迟和 MOTD
- 新增一键连接功能，自动启动游戏并加入选中服务器
- 支持分页加载、导入/导出、删除确认、IP/端口验证
- UI 全面优化：增大按钮尺寸、优化字号层级、状态改用胶囊标签+延迟颜色编码、对话框加宽
- 移除游戏类型和分组功能，精简服务器编辑界面
- 切换到服务器收藏页时自动刷新状态，离开时停止后台轮询
- 刷新时延迟位置显示加载进度条，离线服务器显示 "--" 占位
- 延迟、在线人数、MOTD 信息始终可见，不再仅在线时显示
