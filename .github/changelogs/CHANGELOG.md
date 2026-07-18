 ## [v1.0.0-rc.5] - 2026-06-xx

### 新增
- 依赖模组版本兼容性校验：解析Maven风格版本范围，按Error/Warning分级报告不兼容依赖，并提供解决建议
- SSL证书验证开关和文件哈希校验开关（设置-通用-安全）
- 统一HttpClient工厂，所有网络请求遵循SSL验证配置

### 修复
- 修复MOD/Shader Pack开关状态无法正确显示的问题：移除_isToggling重入标志，文件操作失败时不再修改字段，确保双向绑定能自动回滚UI
- 修复ZIP解压路径遍历漏洞（Zip Slip），所有解压操作增加路径验证

### 新增
- 文件哈希校验功能，下载后自动验证SHA-1，设置中可开关
- 模组冲突检测，解析JAR元数据检测重复ID/缺失依赖/加载器不兼容，启动时冲突弹窗确认
- 模组列表刷新按钮，版本隔离切换后自动刷新
- 快捷操作按钮紧凑排列，Shader Pack 管理移至独立标签页
- UI/UX系统性优化：重新定义色彩规范（深色/浅色模式）、圆角、阴影、按钮/输入框/卡片基础样式
- 窗口质感风格切换：支持亚克力、磨砂玻璃、纯色扁平、悬浮卡片4套质感，在外观设置中即时切换
- 设置页重构：改为左侧二级导航+右侧卡片内容布局，选中项显示绿色竖条指示器
- 更多页面Tab下划线指示器样式：选中态绿色文字+底部绿色指示线
- 导航栏选中态改为绿色左侧指示条+浅绿背景
- 页面过渡位移动效（PageSlide垂直方向220ms）
- 按钮按压缩放反馈、输入框聚焦绿色边框、Slider滑块悬停放大
- 骨架屏加载占位样式、统一空状态样式
- 对话框入场缩放动画
- 引入Velopack自动更新框架，支持增量更新(delta包)和自动下载安装
- Velopack更新失败时自动降级到原有GitHub API检查方式，打开下载页面
- GitHub代理Velopack更新源（GitHubProxyVelopackSource），自动为GitHub下载URL添加镜像代理前缀
- 版本详情界面顶部导航栏（基本信息/版本设置/存档/Mod/Shader 五个标签页），提升界面简洁度
- 版本内存自定义功能：每个版本可独立配置最大/最小内存，持久化至版本目录下 OMCL/init.json，未配置时回退全局设置
- 版本描述属性：安装新版本时按"版本类型,版本,加载器+OptiFine"格式自动生成默认描述，并支持在版本详情界面自定义编辑
- 版本级配置统一存储：分组、隔离、内存、描述等信息集中保存至各版本目录的 OMCL/init.json
- 版本详情高级管理板块：卡片式布局，包含导出启动脚本、补全文件、删除版本三个功能按钮
- 导出启动脚本：生成 Windows 批处理(.bat)或 Shell 脚本(.sh)，包含完整 Java 启动参数，支持自定义保存路径
- 补全文件：自动下载缺失的库文件和资源文件，无需重新下载整个版本
- 删除版本：红色危险按钮样式，点击后二次确认防止误操作，删除后自动返回版本列表
- 版本下载列表显示版本描述文本（灰色、最多两行、无描述时自动隐藏）
- 版本列表和资源列表无限滚动：滚动到底部自动加载更多内容，移除版本列表 50 条硬限制，资源列表支持分页加载 API 数据

### 修复
- 下载管理器面板无法打开：添加ZIndex=100确保面板渲染在SplitView之上，禁用编译绑定以修复CSS :visible伪类动画兼容问题
- 版本详情页返回时崩溃：修复集合并发修改问题，所有ObservableCollection操作统一走UI线程调度，返回时取消后台加载任务
- 主页点击版本详情按钮时崩溃：InstanceViewModel.LoadAsync中I/O与UI更新分离，后台线程收集数据后通过Dispatcher.UIThread派发ObservableCollection更新
- 版本管理页大量绑定错误：将嵌套DataTemplate中的$parent[UserControl]改为x:Name元素引用，避免DataContext未就绪时产生绑定错误日志
- 全局绑定错误消除：ViewLocator.Build()创建View时立即设置DataContext，解决所有页面初始化时#ElementName.DataContext为null的绑定报错
- 更多页面标签页Content类型转换错误：TabControl.ContentTemplate中的ContentPresenter改用ContentControl，避免父级DataTemplate的x:DataType编译绑定强制类型转换
- 游戏日志窗口关闭时递归调用Shutdown导致栈溢出：添加防重入标志
- 截图管理黑屏：使用FilePathToBitmapConverter正确加载本地图片
- 截图按版本筛选无效：修复"主目录"选项筛选逻辑，正确区分主目录和版本隔离目录截图
- 版本详情路径显示错误：始终显示版本目录(.minecraft\versions\xxx)而非游戏根目录
- 版本详情MODS无法显示：统一使用LauncherConfig.GetRunDirectory获取正确的mods目录路径

### 优化
- 版本分组信息从全局配置迁移至各版本 init.json，移除全局分组索引，保留旧数据自动迁移兼容
- 版本隔离信息从全局配置迁移至各版本 init.json，保留旧 version_config.json 自动迁移兼容
- 启动游戏时优先读取版本自定义内存配置，未配置时回退至全局设置
- 导航栏按钮悬浮动画优化：实现 DispatcherTimer 防抖机制（80ms延迟），快速划过时不触发动画，仅最终停留项执行完整动画（缩放1.03x + 背景渐变 + 阴影深度变化），过渡时间0.3-0.35s
- 下载管理器UI重构：任务卡片增加状态色指示条和类型标签，进度条加高至6px，状态色区分下载中/已完成/失败/已取消，新增空状态引导页，底部栏新增任务统计摘要
- 资源列表：移除下载按钮，详情按钮改为accent样式并替代下载按钮位置
- 资源详情页加载器选择框：修复重复选项（GroupBy改用大小写不敏感比较器），修复点击无响应（LoaderFilterItem重写Equals/HashCode使ListBox正确匹配重建后的选中项）
- Java检测：路径去重（规范化路径+仅使用javaw.exe），扩展厂商识别（Dragonwell/Zulu/Liberica/Temurin/Corretto等），新增更多JDK安装目录和注册表路径；选择框显示安装路径（次要文本色）
- 服务器管理：移除状态刷新相关的通知推送，避免频繁通知刷屏
- 通知样式预览窗：增大模拟窗口尺寸（宽度+29%），内部元素等比放大，更接近实际窗口比例
- 全局字重优化：页面标题 Bold→SemiBold，节标题和设置项标签 SemiBold→Medium，按钮 SemiBold→Medium，徽章和强调符号保持不变
- 侧边栏导航重构为 SplitView 组件，使用原生动画替代手动宽度动画，提升流畅度和性能
- 侧边栏支持基于窗口宽度的自适应折叠（宽度 < 950px 自动折叠，>= 950px 自动展开），手动切换后保持用户偏好
- 游戏目录列表新增两个不可删除的默认目录：官方目录（平台自适应路径）和启动器目录（启动器根目录\\.minecraft），置顶显示并带有"默认"标识
- 资源详情页前置资源优化：同一MC版本下的公共前置资源提升至版本分组顶部显示，避免重复；无公共前置时保持原布局
- 修复前置资源名称解析失败显示"Mod #xxxx"的问题，批量获取失败后自动逐个回退获取
- 修复资源详情页筛选切换时版本组重复渲染及交互失效的问题
- 设置界面通知样式预览改为绘制完整启动器窗口缩略图，直观区分居中和右下角两种模式
- 插件系统新增全局事件：VersionInstalling（版本安装开始）、VersionInstalled（版本安装完成/失败，含版本目录信息）、AccountChanged（账户变更，含变更类型）、DownloadProgress（下载进度实时更新）
- 插件系统新增自定义命令API：RegisterCommand/UnregisterCommand，主页卡片支持 command: 前缀执行插件自定义逻辑
- 插件标签页支持自定义UI内容（方案A）：RegisterTab新增重载支持传入Avalonia Control，插件可直接渲染自定义界面
- 资源详情页加载器图标改用内置 PNG 图标，新增加载器快速筛选栏
- 侧边栏导航图标改用 SVG 矢量图标替代 Emoji，提升跨平台渲染一致性；导航图标文件统一管理在 Assets/SidebarIcons 目录，文件名为 dashboard/multiplayer/accounts/versions/resources/settings
- 修复 SVG 图标颜色跟随主题色问题，BitmapAssetValueConverter 新增 SVG 支持并载入时自动替换 currentColor 为当前 TextBrush 颜色
- 修复导航项 SVG 图标与 Emoji 回退同时显示的重叠问题（NotConverter → NullToBoolConverter Invert）
- 修复 LocalVersionService 中 vanilla 拼写错误（vanilia → vanilla_snapshot.png）
- 修复 PluginIconConverter 引用 default_plugin.png 但实际文件为 default_plugin.svg 的问题，新增 SVG 加载及主题色支持
- 账户类型图标标准化：创建 Assets/AccountIcons 目录，将中文名 SVG 统一重命名为 microsoft/yggdrasil/offline，EnumToChineseTextConverter 新增 IconPath 参数返回 SVG Image，微软图标保留彩色、其余跟随主题色
- 修复 yggdrasil.svg / default_plugin.svg / logo.svg XML 声明导致 MSG_ELEMENT_NOT_DECLARED 报错的问题，移除 <?xml?> 和 <!DOCTYPE> 声明
- 修复账号管理添加账号按钮状态无变化问题：新增 IsYggdrasilLoginRunning 登录状态属性，外置登录按钮操作期间自动禁用；离线添加按钮在面板打开时自动禁用，防止重复点击
- 修复浅色主题下 SVG 图标颜色适配问题，EnumToChineseTextConverter / BitmapAssetValueConverter / PluginIconConverter 统一在加载时替换 currentColor 为主题 TextBrush 颜色
- 修复主题切换时 SVG 图标颜色不更新的问题：Converter 新增 IMultiValueConverter 实现，XAML 改用 MultiBinding 绑定 ActualThemeVariant
- 修复 SVG 图标颜色：深色主题用 #FFFFFF，浅色主题用 #000000，直接检查 ActualThemeVariant 而非依赖 TextBrush 资源
- 账号管理添加账号按钮改用 SVG 图标（microsoft.svg / yggdrasil.svg / offline.svg），替换硬编码 Path 元素
- 设置界面重组：将"启动器"标签拆分为"外观"和"通用"，游戏启动行为归入游戏设置，相关设置项合并为分组卡片并添加分区标题
- 主页底部按钮布局优化：齿轮按钮与启动按钮之间增加间距，齿轮按钮禁用状态使用暗色背景和低透明度区分
- 版本管理界面布局优化：压缩标题区高度，标题与描述合并为单行；减小版本列表项内边距和间距，提高信息密度
- 版本分组功能：支持自动/可装MOD/常用版本三个系统分组和自定义分组，版本管理页添加分组标签栏和分组管理弹窗，实例管理页添加分组选择下拉框，版本目录下自动创建OMCL/init.json存储分组信息
- 分组展示改为下拉栏风格：每个分组独立展开容器，显示分组名称、描述和版本数量；移除"全部"选项和版本管理页的分组管理按钮，分组管理入口迁移到版本详情页下拉框中
- 修复版本列表无法滚动的问题：调整Grid行定义为Auto,*，分组列表放入*行
- 自动分组优化：界面不显示"自动"分组栏，自动分组的版本按规则归类到可装MOD/常用版本/不常用版本
- 新增"不常用版本"系统分组：超过30天未游玩或从未游玩的版本自动归入此分组