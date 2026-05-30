## [v1.0.0] - 2026-05-xx

### 新增
- 通知样式切换：支持居中显示和右下角显示两种模式，设置界面提供直观的预览图对比
- 右下角通知自动关闭：可配置自动关闭时间（3-30秒，默认5秒），关闭动画平滑自然
- 版本管理页新增侧边栏式游戏目录管理，支持多目录切换、添加、删除
- 目录删除支持确认对话框（含不可逆警告），永久删除文件夹并自动切换到下一个可用目录
- 游戏目录切换后自动同步更新设置配置，并刷新已安装版本列表
- 下载路径与选中游戏目录动态关联，新下载版本自动存放到当前目录
- 目录切换支持加载状态提示和权限不足等异常处理
- 适配Linux和macOS平台，支持多平台构建和运行
- GitHub CI构建支持Linux和macOS，Release自动发布多平台版本
- 更新系统支持多平台资产匹配

### 优化
- 通知组件按类型配色指示条移至标题左侧，改为4px竖排垂直线条，不干扰标题可读性
- 进度条新增平滑插值动画，消除加载过程中的卡顿跳跃，过渡自然流畅
- 通知组件新增按类型差异化配色方案：右下角色指示条，错误红/成功绿/警告橙/信息蓝，低饱和度低明度
- 右下角通知组件优化：采用400ms缓动滑入动画，移除关闭按钮，宽度调整为300px
- 游戏目录选择重构为左侧滑出式边栏面板，减少界面空间占用
- 将游戏目录设置从设置页迁移至版本管理模块，优化用户操作流程
- 修复多个空引用警告和async/await问题
- 清理未使用的using指令、字段和成员
- GameLauncher代码优化：缓存JsonSerializerOptions、移除未使用参数、string.Contains改用char重载、HashSet.Add替代Contains+Add
- FluentAnimationService集合初始化简化为集合表达式
- CurseForgeService代码优化：缓存JsonSerializerOptions、简化new表达式和集合初始化、移除冗余using
- ModpackInstallService代码优化：移除冗余using、缓存JsonSerializerOptions、移除未使用参数、简化类型名称和集合初始化
- GameLogWindow/ServerManager/VersionDetailViewModel代码优化：使用GeneratedRegex、范围运算符、集合表达式、static方法、Memory重载等
- 磁盘缓存过期时间由30分钟调整为1个月
- Java检测适配Linux/macOS，支持各平台常见Java安装路径
- 游戏目录默认路径适配各平台（Windows: %APPDATA%, macOS: ~/Library/Application Support, Linux: ~/.minecraft）
- 打开文件夹功能跨平台适配（Windows: explorer, macOS: open, Linux: xdg-open）
- Natives库提取和规则判断适配当前操作系统
- 路径分隔符使用Path.DirectorySeparatorChar替代硬编码反斜杠

### 修复
- 修复侧边栏目录项删除按钮不可点击的问题（移除!IsDefault绑定限制）
- 修复侧边栏目录项文件夹图标不显示的问题（Path元素缺少Fill属性）
- 修复默认游戏目录在Custom模式空路径时错误回退到运行目录的问题
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
