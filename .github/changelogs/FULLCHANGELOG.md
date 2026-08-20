# Full Changelog

本文件记录所有版本的更新历史。

## [Unreleased] - 2026-08-20

### 修复
- 【导航栏】修复多人联机图标显示异常的问题，替换为更清晰的图标

## [v1.1.0-beta.3] - 2026-08-19

### 新增
- 【主页/设置】主页可以自定义了！主页组件化，支持插件拓展，设置页可以自定义主页卡片的布局
- 【迁移】迁移完成后显示迁移详情
- 【账号】敏感信息加密处理 (#2) @Commandpr
- 【设置/外观】支持自定义全局强调色
- 【设置/外观】新增圆角/密度/动画级别的个性化外观设置
- 【设置/外观】支持自定义背景图片
- 【设置/外观】支持自定义界面字体与字重
- 【全局】内置 HarmonyOS Sans SC 作为启动器默认字体
- 【插件】插件加载时校验元数据(plugin.json/id/依赖/最低版本)，LauncherVersion用真实源，市场安装改增量加载
- 【插件】新增配置/导航/下载状态/异步钩子API

### 修复
- 【迁移】补加了相关图标
- 【插件】修复部分API和钩子，补发了GameLaunched/GameClosed/VersionDownloaded事件
- 【插件】插件卸载清理启动钩子与事件订阅

### 优化
- 【更多】重构了插件界面，支持markdown格式解析
- 【主页】主页视觉与空状态优化，欢迎卡片跟随强调色、卡片圆角与图标规范化、启动按钮禁用态与账号/版本引导入口
- 【主页】移除启动时无用远程版本清单请求，头像渲染结果缓存复用，账号选中无变化时跳过配置写盘
- 【主页】卡片区独立滚动，账号/版本/启动操作区固定底部常驻
- 【主页】统一导航映射、清理主页死代码、页面直连强类型引用替代标题查找、欢迎卡片ID常量化

### 其他
- 【打包/构建】取消单文件打包发布选项
- 【插件】更新了插件API文档

## [v1.1.0-beta.2] - 2026-08-15

### 新增
- 新增首次启动欢迎窗口：首次启动先显示欢迎窗口（流程不过完不显示主界面），开场动画参考 ClassIsland 的 OOBE 实现（方块翻入字母 + 品牌方块标记 + 间距收拢），随后欢迎内容淡入
- 开发者控制台新增 welcome / welcome reset 命令，可手动打开欢迎窗口或重置首启标记
- 新增实例级 JVM 参数与 Java 路径设置：版本设置页可为单个版本自定义，启动时实例配置优先于全局
- 新增数据迁移：支持迁移 PCL2 和 Hello Minecraft! Launcher 数据，导入应用设置（文件夹、Java、内存、JVM、下载等）与各版本游戏设置（内存、隔离、实例级 Java/JVM 等）
- 开发者控制台新增 notify 命令，可发送各类型测试通知验证样式与动画

### 优化
- 主窗口改用系统原生标题栏（原生窗口按钮与拖拽）；关闭主窗口会正确退出应用，并修复"启动后关闭启动器"只关窗不退出进程的问题
- 崩溃窗口、游戏日志窗口、开发者控制台窗口改用系统原生标题栏（原生窗口按钮与拖拽）；关闭崩溃窗口即退出应用（开发者控制台 crash 指令的预览窗口不受影响）
- 欢迎窗口改用系统原生标题栏（原生窗口按钮与拖拽）；未完成流程关闭时仍弹确认框，确认后才退出应用
- 通知提示框迁移到 FluentAvalonia InfoBar：严重级别语义色与图标随深浅主题自动适配（替换原硬编码色值），进度/倒计时进度条贴合卡片底部，所有位置均提供关闭按钮
- 通知动画重构：移除 ViewModel 内的 Timer 帧动画，改用 XAML 过渡动画驱动进场/退场，右下角模式保留右滑关闭（拖拽跟手、松手回弹）
- 通知服务优化：悬停暂停自动关闭倒计时，居中横幅按通知类型给足阅读时长（错误 5 秒/警告 4 秒），倒计时改用 Stopwatch 消除漂移，位置切换时同步更新已显示通知
- 设置页通知预览改为真实 InfoBar 样式，自动关闭时间改用 NumberBox，选中高亮改用启动器强调色
- 版本实例页 Tab 选择栏接入主题色：tab 条与顶部标题栏同色，选中 tab 主题绿加粗并与内容区背景融合，消除 Fluent 默认暖灰与主题的隔阂（深浅色模式均适配）

### 修复
- 修复版本实例页与账号管理页打开时内联弹窗闪现的问题（分组管理/外置登录弹窗不再闪一下）


## [v1.1.0-beta.1] - 2026-08-14

### 修复
- 修复生产环境下数据目录落在 current 内的问题：Velopack 部署的 current 目录在解压部署或文件系统不支持 junction 时是普通文件夹，原逻辑只识别 junction/symlink 导致 OMCL 与 .minecraft 数据目录写入 current 内、更新时被整体覆盖；现只要运行目录名为 current 即自动向上退一层
- 修复插件目录、图标缓存、MciLm-link 二进制目录等仍写入 current 内的问题：这些调用点绕过基础目录解析直接使用 AppContext.BaseDirectory，统一改用 VersionInfo.GetAppBaseDirectory()
- 修复更新服务无法检测不同通道更新（正式版/测试版/预发布版/预览版）的问题：客户端请求的 Velopack 通道名缺少 RID 前缀，与发布端生成的 feed 文件名（如 releases.win-x64-pre.json）不匹配，导致检查更新永远返回"已是最新版本"
- 修复分组管理弹窗（ContentDialog）内绑定错误：弹窗内容在弹出层中渲染丢失 DataContext 继承，改为显示前显式绑定到当前版本实例 ViewModel
- 修复分组管理弹窗内重命名/删除操作无响应的问题：自绘对话框在 FluentAvalonia ContentDialog 弹出层下方渲染导致交互失效，统一改用 ContentDialog 后解决
- 修复插件相关单元测试偶发失败的问题：多个测试类共享 PluginContext 静态回调（OnTabRegistered 等），xUnit 默认跨测试类并行执行导致回调被并发覆盖、断言随机失败，现禁用测试程序集并行执行保证确定性
- 修复资源中心中文搜索失效的问题：MOD 翻译数据此前仅作为 Avalonia 资源打包进 Desktop 程序集，Core 翻译服务在打包/生产环境读取不到，现改为嵌入 Core 程序集；Modrinth 条目的翻译查找改用兼容双来源的 ID 索引，中文名/缩写搜索结果在混合源模式下不再被模糊过滤误删
- 修复资源详情页点击依赖跳转后无法返回上级模组的问题：详情页改为导航栈管理，返回时逐级退回上级模组，退回列表时才清空

### 新增
- 插件 API 新增目录获取接口：`LauncherBaseDirectory`（启动器基础目录，自动跳出 current）、`LauncherDataDirectory`（OMCL 数据目录）、`GameDirectory`（当前游戏目录），均已正确处理 Velopack 部署路径
- 账号管理页新增功能：登录令牌过期提示（即将过期/已过期/状态未知，按状态着色），打开页面时自动刷新过期令牌；账号卡片展示最近使用时间、邮箱、认证服务器信息；支持按用户名搜索筛选账号；删除默认账号时提示自动接任的默认账号；新增账号后自动滚动定位并短暂高亮

### 优化
- 账号管理页交互优化：刷新状态改为每账号独立（刷新一个账号不再禁用其他账号的按钮），账号列表改用包装视图模型承载每项状态，删除操作进行中禁用操作按钮防重复点击，状态提示按信息/成功/警告/错误分级着色，账号卡片文本窄窗口自动省略
- 账号管理页性能优化：账号类型图标按类型与主题缓存（避免每次绑定重复解析 SVG 换色），头像加载任务去重，列表同步改用字典查找降低复杂度
- 账号管理页代码质量优化：头像加载抽离为 AccountAvatarService（ViewModel 不再直接依赖 Bitmap/AssetLoader），主页账号刷新通知改为事件解耦（不再按导航项标题查找页面实例），离线账号查重校验收敛到服务层
- 账号管理页安全与可访问性优化：外置登录密码在登录完成/取消后立即清空，登录提示改用 InfoBar 适配深浅主题，图标按钮补充无障碍名称
- 打开账号页时自动刷新过期令牌不再弹出状态提示（改为静默刷新，仅账号卡片显示转圈反馈）
- 版本实例界面迁移到 FluentAvalonia 组件：顶部导航标签栏改用 TabView（图标改用 PathIconSource），内存配置输入框改用 NumberBox，分组管理弹窗改用 ContentDialog，OptiFine 兼容性警告改用 InfoBar
- 全局信息框（信息/成功/警告/错误/询问/输入对话框）改用 FluentAvalonia ContentDialog，移除自绘对话框覆盖层
- 版本实例页标签栏禁止拖动与重排序，避免误操作改变标签顺序
- 版本实例界面统一危险色语义：硬编码红色（#FF5252/#E74C3C 等）改用主题 DangerBrush / DangerSoftBrush 资源
- 版本实例页图标缓存改用稳定哈希（SHA256）生成文件名，修复 string.GetHashCode 跨进程随机化导致缓存失效、每次打开实例都重新解压图标的问题
- 版本实例页分组管理入口改为独立按钮，不再隐藏在分组下拉框中
- 内存配置新增联动提示：最小内存不小于最大内存或超过最大内存 1/4 时显示警告
- 版本实例页加载态改用骨架屏占位，替代全屏遮罩
- 设置页全局内存配置新增联动提示：最大内存不大于最小内存时显示警告
- 账号管理页 UI 迁移到 FluentAvalonia：外置登录弹窗改用 ContentDialog（真正的模态对话框，支持 ESC/点击遮罩关闭），账号列表改用虚拟化 ListBox，添加账号改为 DropDownButton 下拉菜单，刷新指示改用 ProgressRing，状态提示改用 InfoBar，新增无账号空状态引导，并移除未使用的 YggdrasilLoginWindow 死代码
- 资源下载页 UI 迁移到 FluentAvalonia：资源类型/来源切换改为分段选择按钮，搜索框支持回车触发与一键清除，结果列表改为卡片式且整行可点击，条目新增来源徽标（CurseForge/Modrinth），新增空状态引导，搜索失败/超时改用 InfoBar 提示
- 资源详情页 UI 迁移到 FluentAvalonia：头部元信息图标化并标注资源来源，摘要支持展开/收起，加载器筛选改为胶囊式选中态，公共依赖改用 InfoBar 展示，版本文件条目统一卡片样式并新增下载中/已下载状态反馈，加载态改用 ProgressRing，下载/安装结果改用 InfoBar 分级提示
- 资源中心列表细节优化：移除列表项快捷下载按钮，仅保留"详情"操作；来源标签（CurseForge/Modrinth）紧跟资源名展示，标题按剩余空间省略，长内容不再横向挤压"详情"按钮，卡片固定高度保证各行按钮对齐；顶部筛选下拉框缩小尺寸
- 资源中心混合源（全部）搜索结果按匹配度归并排序：高分条目聚在一起，同分时 CurseForge 与 Modrinth 轮流穿插，不再按来源分块堆叠

## [v1.0.1-pre.1] - 2026-08-14

### 修复
- 修复"更多"页打开时崩溃的问题：ListBoxItem 主题内伪类样式改用嵌套选择器写法，符合 Avalonia 主题语法规范
- 修复"更多-插件"详情 README 加载竞态：快速切换插件时，旧请求结果不再覆盖当前选中插件的内容
- 修复"更多-截图管理"分页加载失效的问题：滚动到底部时自动加载更多截图，此前最多只显示前 20 张
- 修复浅色主题下导航栏/标题栏/窗口/卡片背景仍为深色的问题：ApplyLightTheme/ApplyDarkTheme 补充 NavBackgroundBrush、TitleBarBackgroundBrush、WindowBackgroundBrush、CardBackgroundBrush 等资源更新
- 修复资源中心搜索结果已显示但骨架屏仍不消失的问题：Modrinth 版本信息改为后台异步加载，不再阻塞搜索完成；搜索增加 60 秒超时兜底；已有结果时立即隐藏骨架屏
- 导航栏图标改用 PathIconSource + SvgToGeometryConverter，图标随 Foreground 变色（选中态变主题色、主题切换跟随文本色），不再使用固定颜色的 ImageIconSource
- SvgThemeHelper 浅色/深色图标颜色改为 TextSecondaryBrush 色值，避免纯黑/纯白对比度过强
- 修复主页卡片导航到"设置/更多"页无效的问题（底部导航项未参与查找）
- 修复安装原版版本时未下载版本文件导致资源补全失败的问题
- 修复下载管理器无法显示补全资源任务的进度详情的问题

### 优化
- "更多-关于"页分组默认只展开"关于"与"免责声明"，"相关链接"和"特别感谢"默认折叠，且二者移至页面下方
- "更多-关于"页改用 FluentAvalonia SettingsExpander 组件：相关链接、特别感谢、关于、免责声明按 Fluent Design 分组卡片展示，链接项整行可点击，图标统一为单色
- "更多-关于"页排版重构：头部改为横向紧凑布局（logo 缩小、版本/更新通道与检查更新按钮同排），相关链接与特别感谢改为自适应两列、条目压缩，协议/技术栈/版权信息合并到关于卡片
- 插件列表刷新/筛选后自动恢复之前选中的插件，不再每次清空选中状态
- 截图/服务器收藏列表改用虚拟化 ListBox，大量项目时滚动更流畅、内存占用更低
- 修复插件市场列表嵌套滚动问题：移除 ListBox 外层多余的 ScrollViewer，滚动行为恢复正常
- "更多"页实现懒加载：移除进入页面时一次性加载全部标签数据的死代码，插件/截图/服务器数据只在首次切换到对应标签时加载
- 修复"更多"页标签页内容双重渲染：头部 TabControl 不再自行渲染选中页内容，统一由下方内容区呈现
- 服务器收藏状态刷新优化：配置只加载一次，移除分页渲染时逐条服务器刷屏日志
- 插件详情 README 改为复用共享 HttpClient 并按地址缓存内容，重复查看不再重复下载
- 缩略图加载优化：截图/服务器/版本图标改用限宽 640 解码并缓存，不再全分辨率加载大图，显著降低内存占用
- 更多页搜索/筛选增加防抖与取消：截图、服务器收藏、插件市场三处的搜索输入与筛选操作不再并发执行，快速输入时结果乱序问题消除
- 截图管理支持导出：点击截图卡片"导出"按钮可选择保存位置，替换原先的占位实现
- 移除"更多"页重复的检查更新逻辑（MoreViewModel 中的死代码，约 90 行），统一使用关于页中的实现
- 主窗口导航栏迁移到 FluentAvalonia NavigationView + Frame：替换原 SplitView + ListBox 自绘导航，导航项/底部导航通过 MenuItemsSource/FooterMenuItemsSource 数据绑定生成，内容区使用 Frame 按 ViewModel 实例缓存页面，页面切换带 Fluent 转场动画
- 移除导航栏底部版权标志（含版权条目、模板选择器及相关代码）
- 导航栏图标改用 Assets/SidebarIcons 的 SVG（ImageIconSource 绑定 BitmapAssetValueConverter，随深浅色主题自动换色），并新增 more.svg 补充"更多"页图标
- 图标主题换色改为使用绑定传入的主题（此前 SvgThemeHelper 读取 Application 当前主题，浅色模式下可能误用深色白色图标）；账户/插件图标转换一并同步
- 设置界面重构：采用 FluentAvalonia 的 NavigationView、Frame、SettingsExpander 实现 Fluent Design 风格布局，替换原有卡片式界面
- 设置子页面实例缓存：离开设置页再返回时保留在原分页，且滚动位置等页面状态不丢失
- 资源详情页返回按钮移至页面左上角独立一行，不再与资源标题挤在同一行

## [v1.0.1-beta.1] - 2026-08-12

### 移除
- 移除窗口质感功能及设置选项：删除质感风格切换入口与相关样式文件，窗体统一使用纯色表面

### 优化
- 更新通道修复：发布流程从 tag 预发布段（pre.1/rc.1/beta.1 等）推导 Velopack 通道并写入产物（如 win-x64-pre），客户端全部通道显式映射到对应 feed，测试版/预发布版/预览版用户可正常收到对应通道更新；stable 发布同步生成旧格式 feed 兼容已安装客户端平滑迁移
- 资源中心与版本下载页大列表改为虚拟化 ListBox，滚动加载数百项不再卡顿
- 图标加载统一缓存：PNG 按地址、SVG 按地址+主题缓存，消除每次绑定评估重复解码
- 下载任务列表启用编译绑定；下载进度上报 100ms 节流，减轻 UI 线程压力
- 文件哈希校验开关改为缓存读取，消除资源下载时对每个文件重复读配置
- 游戏完整性检查与启动结果改为调用级结果对象，消除静态可变状态并发覆盖
- 游戏启动命令行参数（用户名/服务器地址/自定义 JVM 参数/令牌）统一按 Windows 规则转义，防止注入
- 下载任务管理器支持 UI 派发器注入，集合更新回到 UI 线程
- 版本清单加 10 分钟内存缓存，避免安装流程中重复下载数 MB manifest
- 本地版本扫描不再对整份 JSON 做 ToLower 复制，JSON 序列化选项与正则表达式统一静态化
- 删除死代码：旧版 LocalVersionService、串行库下载、未使用的对话框方法
- 版本安装后的资源补全任务去掉多余线程包装，CTS 随任务自动释放
- UI 简约化重构：引入 FluentAvalonia 主题，新增三级表面色阶（LayerFillColor*）通过固定明度差表达层级，解决浅色模式层次感丢失
- 主色系收敛：12 个绿色变体别名统一指向单一 AccentBrush，质感风格从 4 种合并为 Mica / Flat 两种
- HomeView 欢迎卡片去除装饰几何元素，对话框圆角统一到 OverlayCornerRadius
- 导航栏去除选中项左侧绿色竖条避免与图标重复，折叠态布局修复
- FluentAvalonia 强调色强制锁定为绿色，内容区改用中层表面色与导航栏形成明度差
- 质感风格改用纯色明度差表达层级，Mica 模式加强卡片阴影
- 窗体启用 Mica 透明背景，标题栏/导航栏/内容区半透明透出系统模糊，整个窗体具备质感
- HomeView 去卡片化：OtherCards 去除阴影扁平化，底部启动栏改用分隔线区分，减少审美疲劳
- 导航栏折叠按钮居中修复：改用固定 48px 图标列，避免 NavBorder 边框干扰对齐
- VersionDetailView 去卡片化：版本信息和加载器选择去除 Border 卡片，改用分隔线+标题分区
- ResourcesView 去卡片化：搜索栏和结果列表去除 Border 卡片，结果项改用分隔线分隔的扁平列表
- 导航栏折叠按钮居中修复：改用 48px 固定图标列，移除绿色竖条避免重叠
- 按钮闪烁修复：删除全局 Button hover Opacity/scale，nav-item-hovered 去处 scale 缩放和 BoxShadow
- InstanceView 已安装版本去卡片化：基本信息/描述/存储/快捷操作/高级管理/版本设置均去除外层卡片
- ResourcesView 滚动条右移 Padding 避免与详情按钮重叠
- ModDetailView 去卡片化：信息密度提升，版本列表去除 Expander，改用紧凑卡片+分隔线布局
- 导航栏折叠图标居中修复：将 NavBorder 右边框移至内容区左边框，使导航内容区与面板宽度一致

### 新增
- Velopack更新通道切换：支持正式版/测试版/预发布版/预览版四个通道，在设置中切换并在"更多"页面显示

### 修复
- 修复按钮悬停/按下背景色生硬跳变：模板内容面板统一添加 0.15s 背景渐变过渡，移除无用的全局 Opacity 动画
- 修复版本详情页返回时崩溃（ObjectDisposedException）：安装流程结束后 CTS 已释放，返回操作再次调用 Cancel 抛异常，统一改为安全取消并置空字段
- 修复崩溃窗口错误信息编辑框大小异常：只读长文本改用 ScrollViewer + SelectableTextBlock，布局稳定且支持选择复制
- 开发者控制台输出框同步改用 SelectableTextBlock，高频输出不再触发 TextBox 全量重渲染
- 修复插件市场远程图标"下载即弃"：图标下载后从未显示，改为缓存 + 并发去重 + 下载完成自动刷新
- 修复存档选择对话框"确定"按钮始终禁用（转换器绑定错误）
- 修复资源中心加载骨架屏永不显示
- 修复皮肤头像渲染 alpha 格式错误：直通 alpha 像素写入预乘缓冲区导致半透明边缘渲染异常
- 修复 MciLm-link 进程退出回调读到新进程数据的闭包竞争；房间码改用 ArgumentList 防命令行注入
- 修复服务器状态查询中 VarInt 同步阻塞线程池线程，移除逐 chunk 冗余日志
- 修复下载任务镜像表（DownloadBridge）只增不减导致的事件订阅泄漏，补齐取消状态同步
- 修复多个 async void 隐患：导航动画（带取消/防重入）、版本页初始化、通知移除、删除账号命令
- 修复 GameVersionNumber 哈希契约违反：语义相等的版本（如 "1.21" 与 "1.21.0"）哈希不同，导致 HashSet/Dictionary 失效
- 修复下载管线多处 HttpResponseMessage 未释放：下载版本 JSON、资源文件、库文件时响应对象泄漏
- 修复多处 CancellationTokenSource 只取消不释放：资源搜索、版本安装/加载、启动流程、设置自动保存
- 修复事件订阅泄漏：设置页系统主题监听、下载管理器、主页卡片集合变更订阅在退出时未退订
- 修复头像/图标加载时资源流与旧位图未释放
- 修复插件系统事件与启动钩子表并发读写线程安全问题（与命令表一致的加锁保护）
- 修复游戏启动取消时已启动的游戏进程不终止、令牌刷新超时后后台任务游离
- 修复 Java 检测进程挂起时检测线程永久阻塞（读取无超时、超时不杀进程）
- 修复 ModDetail/Resources 详情页在后台线程修改 ObservableCollection 与创建位图导致的跨线程 UI 风险
- "更多"页面更新通道显示不同步：切换到"更多"标签时自动刷新通道信息
- PluginContext线程安全：_pluginCommands字典并发访问加锁，修复并行测试崩溃
 ## [v1.0.0] - 2026-07-18

### 修复
- JVM参数兼容性：在 GameLauncher.ShouldSkipJvmArg 中过滤高版本JDK实验性参数 UseCompactObjectHeaders（JDK 24+引入），避免在Java 21等低版本上启动时报 "Unrecognized VM option" 错误；同时将 config.JvmArguments 的拼接逻辑改为逐参数过滤，确保用户自定义JVM参数也应用相同的兼容性过滤
- NeoForge版本号解析：NeoForgeVersion.MinecraftVersion 严格校验三段式版本号格式（{MC主版本}.{MC次版本}.{构建号}），拒绝非标准格式（如4段的26.2.0.25-beta会被识别为MC 1.26.2的错误情况）；当无法解析时提供包含格式说明和官方版本列表链接的详细错误消息
- SafeZipExtractor ZIP条目提取：ExtractEntryWithNameOnly 方法原本错误地拒绝任何 FullName 含路径分隔符的ZIP条目，导致NeoForge安装器中的 data/client.lzma 条目无法提取；修正为允许ZIP条目本身含路径（正常情况），只使用 entry.Name（文件名部分）作为输出路径，保证输出不会逃逸出 destinationDir；同时加强路径遍历防护，使用 fullBaseDir + Path.DirectorySeparatorChar 严格前缀匹配；更新相关测试用例验证新行为
- 资源文件下载文件占用错误：AssetsDownloadService 中 FileStream 原本使用 FileShare.None 独占模式打开文件，当杀毒软件（如 Windows Defender）或 Windows 搜索索引服务扫描新建文件时会短暂锁定文件，导致并行下载多个资源时出现 "The process cannot access the file ... because it is being used by another process" 错误；修复为使用 FileShare.Read 允许其他进程读取，并新增文件访问冲突的专用重试机制（最多5次，每次递增200ms等待），在重试范围内自动恢复，避免直接失败
- 资源下载性能优化：HttpClientHandler 替换为 SocketsHttpHandler，MaxConnectionsPerServer 从 32 提升到 64，启用 HTTP/2 多路复用（EnableMultipleHttp2Connections=true，DefaultRequestVersion=Version20），对 BMCLAPI 等同一源的大量小文件下载可显著减少 TCP 连接建立开销；worker 模型从原本的"每 worker 串行 await 单个下载"改为"每 worker 用 SemaphoreSlim 控制并发数为 4"（总并发量 = maxThreads × 4 = 32），充分利用 await 期间的 CPU 空闲；FileStream 缓冲区从 8KB 提升到 64KB，减少小文件 I/O 系统调用次数；将单资源下载逻辑抽取为 DownloadOneAssetAsync 方法，统计与进度上报抽取为 OnAssetDone 方法，代码更清晰

 ### 新增
- 依赖模组版本兼容性校验：实现 ModVersion/ModVersionRange 解析Maven风格版本范围（[1.0.0,2.0.0)等），按主版本号差距自动分配Error/Warning级别，并在UI中展示冲突描述与解决建议
- Shader Pack图标显示：LoadShaderPacks时从光影包zip根目录提取pack.png/logo.png/icon.png到临时缓存目录，ShaderPackInfo新增IconPath属性，InstanceView的Shader标签页用32x32 Border承载图标（无图标时回退到默认图形）
- 材质包管理页面：InstanceViewModel新增ResourcePackInfo模型和ResourcePacks集合，LoadResourcePacks扫描resourcepacks目录下的.zip和.zip.disabled文件；ToggleResourcePack命令通过重命名文件追加/移除.disabled后缀实现启用/禁用；DeleteResourcePackAsync带确认对话框；ExtractResourcePackIcon从zip根目录提取pack.png/logo.png/icon.png到运行目录\OMCL\cache\resourcepack_icons；InstanceView导航栏新增"材质包"标签（第6个），内容区展示图标、名称、大小、删除按钮和开关
- 存档管理优化：WorldInfo模型扩展CreationTime/GameVersion/WorldSizeBytes/IconPath字段和WorldSizeDisplay计算属性；CollectWorlds读取目录创建时间、递归计算世界大小、读取icon.png作为存档图标；实现轻量级NBT解析器从GZip压缩的level.dat中提取Data.Version.Id版本信息；InstanceView存档Tab改用40x40图标Border+多行详细信息布局（版本/大小/创建时间/修改时间）
- SSL证书验证开关：设置-通用-安全中添加"跳过SSL证书验证"选项，默认关闭（验证证书），开启时显示安全警告
- 文件哈希校验开关：设置-通用-安全中添加"文件哈希校验"选项，默认开启
- Velopack更新通道切换：设置-通用-启动器中新增"更新通道"选项，支持正式版/测试版(Beta)/预发布版(RC)/预览版(Preview)四个通道，切换后立即重新初始化UpdateManager（带AllowVersionDowngrade），"更多"页面版本号下方显示当前通道
- 统一HttpClient工厂：所有网络请求通过HttpClientFactory创建，统一遵循SSL验证配置
- 单元测试套件：新增 ModVersionRangeTests/NbtReaderTests/ModConflictDetectorTests 三个测试类，覆盖版本范围解析、NBT解析、冲突检测共218个测试用例
- 性能基准测试：新增 PerformanceBenchmarkTests，覆盖 100/500 Mod 冲突检测、100 个 level.dat 解析、1000 次版本范围解析场景
- 插件系统扩展API：IPluginContext 新增 5 个方法：LogMessage 写入统一日志；GetInstalledVersions 返回已安装版本只读列表；GetCurrentAccount 返回当前账户精简信息（不含令牌）；RegisterGameLaunchHook 注册启动生命周期钩子（BeforeLaunch/AfterLaunch/OnExited/OnCrash，BeforeLaunch 阶段可 CancelLaunch 中止启动并追加 JVM/游戏参数）；RequestDownload 提交下载请求（强制 http/https、文件名禁含路径分隔符）。新增 PluginApiModels.cs 包含所有扩展 API 模型与枚举
- 插件系统测试套件：新增 PluginExtendedApiTests（41个用例，覆盖5个新API的正向/异常/边界/集成测试）、PluginCardFlowTests（17个用例，验证自建卡片创建/编辑/删除/交互/批量清理全流程）、PluginPageFlowTests（17个用例，验证自定义页面创建/布局/权限隔离/跨平台/编辑/删除全流程），共新增91个测试用例，测试总数达313个
- README全面更新：补充5项特色功能描述（四套窗口质感实时切换、Maven风格版本范围依赖校验、JAR元数据驱动模组冲突检测、每版本OMCL/init.json统一配置存储、Velopack增量自动更新+GitHub代理降级），每项含原理/应用场景/使用方法；更新项目架构图反映 Core/Desktop/Tests 三层结构与新增文件；新增架构设计原则与插件系统API文档
- 插件开发指南（Plugin-Development.md）全面更新：目录补充5个扩展API锚点；IPluginContext 接口代码示例新增 LogMessage/GetInstalledVersions/GetCurrentAccount/RegisterGameLaunchHook/UnregisterGameLaunchHook/RequestDownload 6个方法签名；API参考章节新增 8-12 五个小节，每个API包含方法签名、参数表、返回值规范、字段说明表、安全约束、异常处理、完整示例代码（含异常处理与多种调用场景）；新增 GameLaunchPhase/GameLaunchHookContext/PluginVersionInfo/PluginAccountInfo/PluginDownloadRequest 5个类型的字段表；新增"启动钩子插件"和"版本备份插件"两个综合示例展示扩展API的实际应用；扩展常见问题章节，新增"插件可以拦截游戏启动吗"/"插件可以获取用户的微软访问令牌吗"/"插件下载文件会被沙箱限制吗"/"插件钩子触发顺序是怎样的"4个FAQ

 ### 优化
- Shader Pack/材质包管理页面新增刷新按钮：RefreshShaderPacks/RefreshResourcePacks命令调用LoadShaderPacks/LoadResourcePacks重新加载列表，与Mod管理页面的刷新按钮一致
- 图标缓存位置统一：Shader Pack/Mod/材质包图标缓存都改到运行目录\OMCL\cache下对应子目录
- Shader Pack图标查找路径扩展：除根目录外增加 shaders/textures/gui 子目录查找，适配不同作者的打包习惯
- 主窗口侧边栏底色统一：ApplyWindowStyleThemeOverride 中亚克力模式 NavBackgroundBrush 透明度由 0.82 改为 1.0、磨砂玻璃模式由 0.6 改为 0.95，与 WindowBackgroundBrush 保持一致；MainWindow 中 NavListBox/BottomNavListBox 设置 Transparent 背景避免ListBox默认背景色覆盖导航栏底色；Theme.axaml 补充 NavBackgroundBrush/NavBorderBrush/TitleBarBackgroundBrush/TitleBarBorderBrush 的默认值定义，避免启动初期资源未就绪时侧边栏透明
- 版本详情页标签栏底色统一：InstanceView 标签栏底色由 SurfaceBrush 改为 BackgroundBrush，与内容区主题背景色保持一致，通过底部边框区分层级
- NBT解析器迁移与健壮性增强：NbtReader 从 ObsMCLauncher.Desktop 迁移到 ObsMCLauncher.Core 项目以提升可测试性；增加 GZip 魔数（0x1F 0x8B）自动检测，支持压缩与未压缩 level.dat；EndOfStreamException 捕获后返回空字符串避免崩溃；解析过程通过 DebugLogger 记录关键节点与异常信息
- 弃用API清理：ModpackInstallService 中两处 SetVersionIsolation(versionDir, true) 调用替换为 SetIsolationMode(versionDir, "enabled")，消除 CS0618 编译警告，参数语义保持一致

 ### 修复
- MOD/Shader Pack开关状态显示问题：移除IsEnabled setter中的_isToggling重入标志，改为先执行文件操作再更新字段，文件操作失败时字段保持不变，双向绑定自动回滚ToggleSwitch状态
- ZIP解压路径遍历漏洞（Zip Slip）：所有ZIP解压操作通过SafeZipExtractor进行路径验证，防止恶意ZIP写入目标目录之外的文件
- 跨平台打开文件夹失效：InstanceViewModel 中 OpenFolder/OpenSavesFolder/OpenModsFolder/OpenShaderPacksFolder/OpenResourcePacksFolder/OpenConfigFolder 6处入口原先直接 Process.Start(FileName=path, UseShellExecute=true) 在 Linux/macOS 下无法打开文件管理器；统一抽取 OpenFolderInExplorer 辅助方法，根据 OperatingSystem.IsWindows/IsMacOS 选择 explorer.exe/open/xdg-open 作为启动程序
- 文件哈希校验功能：下载资产/库文件/客户端JAR后自动验证SHA-1哈希，校验失败删除文件并记录日志
- 模组冲突检测：加载模组时解析JAR内元数据（fabric.mod.json/mods.toml/quilt.mod.json），检测重复ID、缺失依赖、加载器不兼容等冲突，冲突模组红色标记并显示详细描述
- 启动游戏时模组冲突检测：存在严重冲突时弹窗询问是否继续启动，否则终止启动
- 版本隔离切换后自动刷新模组列表
- 快捷操作：版本详情页新增目录快捷跳转（Mod/Shader/资源包/配置），按钮紧凑水平排列
- Shader Pack 管理独立标签页：启用/禁用/删除，从基本信息页移至专用Shader标签
- 模组列表添加刷新按钮
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
vanilla_snapshot- 修复前置资源名称解析失败显示"Mod #xxxx"的问题，批量获取失败后自动逐个回退获取
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
- 移除VersionInitData的CustomData字段

## [v1.0.0-rc.4] - 2026-06-06

### 新增
- 右下角通知支持点击关闭和向右滑动关闭，250ms ease-out退出动画，释放资源
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
- 设置界面重构为左侧导航+右侧内容分栏布局，支持游戏设置/启动器/下载/主页管理四个分类快速切换
- 设置项拆分为独立卡片，提升信息密度和可读性
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
- 统一默认游戏目录路径计算逻辑至 LauncherConfig.GetDefaultAppdataGameDirectory()
- Windows 版本运行时不再出现控制台窗口（OutputType 条件设为 WinExe）
- CI 构建添加 NuGet 包缓存，加速重复构建
- CurseForge 搜索接口接入双层缓存，减少重复 API 请求
- CurseForge 分类列表、文件信息接口新增磁盘缓存
- 启动时预创建缓存目录（OMCL/cache），避免惰性初始化失败静默丢失
- 缓存服务添加 Info 级别日志，便于排查缓存运行状态

### 服务器管理
- Ping 检测升级为真实 Minecraft 协议（VarInt 编解码 + Handshake/Status 响应），返回真实延迟和 MOTD 数据
- 新增一键连接功能，自动启动游戏并加入选中服务器，支持连接进度反馈和错误处理
- 服务器列表支持分页加载、MOTD/版本搜索筛选
- 新增服务器导入/导出功能（JSON 格式），含重复检测
- 服务器编辑添加 IP 地址/端口号格式验证
- 删除服务器添加确认对话框，防止误删除
- 连接按钮在连接中自动禁用，刷新状态按钮防重复点击
- 所有关键操作添加 DebugLogger 日志记录
- UI 全面优化：增大按钮尺寸（16px/20px 内边距）、调整字号体系（名称 17px/标签 13px/内容 12px）、状态改用胶囊标签+延迟颜色编码、工具栏双行布局、对话框加宽至 480px、卡片圆角 14px、空状态图标调整
- 移除游戏类型和分组功能，精简服务器编辑对话框（端口+备注双列布局），移除工具栏分组筛选下拉框
- 切换到服务器收藏页时自动触发 RefreshStatusAsync，离开时停止后台 60 秒轮询（StopAutoRefresh）
- 刷新期间延迟位显示 ProgressBar 加载动画（IsIndeterminate），离线服务器显示 "--" 占位
- 延迟、在线人数（FormattedPlayers 离线显示"离线"）、MOTD 信息始终可见，移除 IsVisible="{Binding IsOnline}" 条件

### 修复
- 修复 Ping 超时时 Task.WhenAny 导致未观察异常引发崩溃（改用 CancellationTokenSource 超时连接）
- 修复 LoadAsync 未初始化 _allFilteredServers 导致首次加载时分页不生效
- 修复 Ping 握手包 BuildHandshakePacket 中协议版本字段被错误填入地址长度值，改为标准 VarInt(-1)，并正确添加地址字符串 VarInt 长度前缀，解决服务器无法解析握手包导致连接失败的问题
- 修复 BoolToColorConverter 和 PingLevelToColorConverter 返回 string 而非 Avalonia.Media.Color（导致 XAML 中 SolidColorBrush.Color 绑定全部失败，卡片数据无法渲染）
- 修复 PingLevelToColorConverter 中 "较差" 对应色值为非法 8 位 hex "#ef4444ff"，改为正确的 "#EF4444"
- 修复 ParseMotd 在 description.text 字段存在但为空时直接返回空字符串，不再继续检查 extra 数组（复合 MOTD 格式），导致 Velocity 代理类服务器 MOTD 显示为空
- 新增服务器图标显示：QueryServerInfoAsync 从协议获取 base64 图标数据，解码后保存为 PNG 文件到 OMCL/cache/server_icons/{serverId}.png，卡片左上角显示 40x40 圆角图标
- 修复 ServerInfo.FormattedVersion 属性：Version 为空时返回"未知"而非显示"版本: "
- 卡片头部布局改为三列（图标 + 名称 + 状态标签），名称垂直居中


## [v1.0.0-rc.3] - 2026-04-30

### 新增
- **游戏日志窗口彩色渲染**
  - 支持 ANSI 转义序列解析和着色
  - 支持日志级别（INFO/WARN/ERROR）自动着色
  - 深浅色主题自适应配色
  - 支持 Minecraft § 颜色代码转换

### 修复
- 修复关闭游戏日志窗口时进程不退出问题
- 修复 CurseForge 镜像源健康检查端点不可用的问题

- **Fluent Design 对话框系统**
  - 现代化设计风格的对话框
  - 支持多种类型（信息、成功、警告、错误、询问）
  - 每种类型配有专属图标和配色
  - 亚克力材质背景、圆角和阴影效果

- **灵动动画效果**
  - 流畅的对话框进入/退出动画
  - 回弹效果的缩放和位移动画
  - 悬停交互效果

- **Fluent 动画服务**
  - 统一的动画 API
  - 高性能动画渲染

- OptiFine 与其他加载器组合时显示兼容性警告提示
- 自定义版本名根据加载器选择自动生成（版本号-加载器_版本-OptiFine_版本）
- 适配 Minecraft 新版本号系统（基于年份的版本号格式，如 26.1）
- 资源下载页面新增"全部"来源选项，可同时搜索 CurseForge 和 Modrinth
- 版本实例管理支持启用/禁用Mod（兼容PCL2，通过.disabled后缀判断）
- 资源详情页新增访问网站按钮，可跳转到CurseForge或Modrinth项目页面
- 社区镜像源(MCIM)支持，可在设置中选择"优先镜像源"或"只使用官方源"
- 镜像源可用性自动检测，启动时和切换选项时执行检测
- Modrinth/CurseForge API和CDN下载自动回退机制（镜像源失败时自动切换官方源）
- 全局User-Agent统一为ObsMCLauncher/{version}格式
- 关于页面添加MCIM (https://www.mcimirror.top/) 社区资源镜像源鸣谢
- **搜索功能优化**
  - 模糊搜索：允许用户输入部分关键词即可匹配相关资源结果
  - 中文搜索：确保对中文资源名称、描述等内容的准确搜索支持
  - 搜索防抖：用户停止输入300ms后自动触发搜索，提升搜索性能

- **前置模组显示功能**
  - 自动识别并获取当前资源所需的前置模组信息
  - 在版本信息区域清晰展示前置模组名称及相关信息
  - 实现前置模组名称的点击跳转功能，点击后可直接导航至对应模组的详情页面
  - 设计明确的视觉标识区分普通资源与前置模组链接，必需依赖显示红色标签，可选依赖显示灰色标签

- 版本下载页面加载器版本下拉框增加加载状态提示（加载中/暂无版本）

### 优化
- 对话框支持自定义样式（圆角、阴影、透明度等）
- 完善主题和样式系统
- 资源详情页版本排序优化：正式版 > Rc > Pre > Snapshot，支持 rc/pre/snapshot 后缀识别
- 重构Forge安装流程：所有操作在.temp目录内完成后才迁移到最终位置，确保原子性
- Forge安装添加完整性验证（JSON可解析性检查、SHA1校验）
- 改进旧版Forge手动安装流程，补充libraries下载
- 消除多处静默异常捕获，添加有意义的错误提示和日志记录
- 修复整合包模式下.temp目录路径嵌套问题
- 优化安装进度系统：分阶段进度展示（加载器安装/OptiFine整合/完成），OptiFine进度纳入总体进度
- Forge/NeoForge安装器运行时实时显示安装状态（下载库/解压/校验/处理组件等）
- 安装取消添加确认对话框，防止误操作
- 全面优化UI/UX设计，提升视觉一致性和交互体验
- 重新设计全局主题配色，加深背景色层次，提升对比度
- 新增danger/ghost按钮样式，统一危险操作和轻量按钮视觉
- 优化滚动条、进度条、输入框等全局控件样式
- 标题栏按钮改用矢量图标，关闭按钮悬停变红
- 导航栏选中项改用品牌色高亮，增强视觉反馈
- 主页欢迎卡片增加装饰元素和阴影效果
- 快速启动区域优化布局，启动按钮增加播放图标
- 设置页改用卡片式分组布局，增加视觉层次
- 账号管理页优化账号卡片样式，默认账号增加标签
- 多人联机页改用大卡片模式选择，提升视觉引导
- 实例管理页优化信息展示和Mod列表样式
- 资源中心页优化筛选栏和结果卡片布局
- 更多页面优化关于页布局和链接样式
- 微软登录授权改为模态对话框形式，添加醒目的复制链接按钮和点击遮罩关闭功能
- 资源下载页面搜索性能优化，支持并行搜索和本地翻译数据库匹配
- 前置模组信息批量加载，减少API调用次数
- 依赖项解析支持CurseForge和Modrinth双平台
- 资源详情页版本信息展示密度优化，增大版本列表显示数量
- 实现双层缓存机制：内存缓存 + 磁盘缓存（OMCL\cache路径），提升资源获取和搜索速度
- 资源页面加载性能优化，消除依赖跳转与正常点击的加载速度差异
- OptiFine版本选择功能修复：单独勾选OptiFine时版本名称动态更新
- 版本下载页面加载器版本下拉框增加加载状态提示（加载中/暂无版本）
- 镜像源可用性按平台独立检测（CurseForge/Modrinth分离），修复某平台镜像失败导致另一平台镜像也被禁用的问题
- 图片加载支持镜像源回退，修复CurseForge图标在国内无法加载的问题
- CurseForge文件列表缓存，减少重复API请求
- 设置页自动保存通知添加1秒防抖，避免Slider拖动时通知狂弹
- ImageCacheService添加100MB缓存大小限制和清理机制
- ResourceCacheService添加GetCacheSize/ClearCache方法用于缓存管理
- MirrorHealthChecker健康检查超时从10秒缩短到8秒，失败后30秒快速重试（而非5分钟）
- 镜像源添加失败计数和状态摘要接口GetStatusSummary
- 版本实例管理页存储路径改为可点击打开文件夹，路径文字使用主题色标识
- 版本实例管理页排版优化：增大间距、调整字体、三栏信息等宽居中

### 修复
- 修复游戏日志窗口无法渲染彩色文字的问题（ERROR/WARNING等日志级别现在有对应颜色）
- 修复资源列表点击详情后资源列表在详情页上置顶的问题
- 修复CurseForge 404错误产生大量ERROR日志的问题（改为WARN级别）
- 修复缓存文件并发访问导致文件被占用的问题（添加文件锁）
- 修复游戏启动后关闭启动器选项不生效的问题
- 修复导航栏图标未居左的问题
- 修复账号管理页面刷新按钮在刷新过程中仍可点击的问题
- 修复设为默认账号后界面更新不及时的问题（GameAccount.IsDefault未触发PropertyChanged）
- 修复设为默认账号时动画不播放的问题（改用Opacity+RenderTransform绑定替代IsVisible）
- 修复刷新账号列表时所有项误触发动画的问题（仅对IsDefault实际变化的项更新属性）
- 修复主页选择账号后账号管理页面默认账号不刷新的问题（在HomeViewModel中通知AccountManagementViewModel刷新）
- 为默认账号标签添加淡入淡出和缩放过渡动画效果
- 版本详情不再显示在左侧导航栏，改为覆盖层显示
- 修复崩溃窗口弹出后立即被关闭的问题
- 修复崩溃窗口可能重复弹出的问题
- 修复崩溃窗口在浅色主题下文字颜色看不清的问题
- 修复版本实例管理界面存储路径、mods路径、存档路径计算错误的问题
- 修复启动游戏检测到依赖缺失时仅提示而不自动补全的问题
- 修复版本下载页面点击开始安装后进度面板遮挡返回按钮的问题
- 修复资源下载页MOD安装目标版本下拉框显示不支持MOD的原版版本的问题
- 修复安装过程中取消按钮无法终止Forge安装器Java进程的问题
- 修复取消安装后未清理临时文件和恢复界面状态的问题
- 修复Forge安装流程未传递CancellationToken导致取消不生效的问题
- 优化安装进度面板布局，居中显示并限制最大宽度
- 优化版本安装页面整体宽度，避免出现水平滚动条
- 安装进度改为覆盖层(Overlay)模式，半透明遮罩+居中弹窗
- 修复取消安装后弹出安装器报错信息的问题
- 修复取消安装后错误显示"安装成功"的问题
- 新增版本名称冲突检测，安装名称与已有版本重复时禁用安装按钮并显示红色提示
- 修复版本隔离模式下资源下载时mods目录不存在的问题
- 主页账号无法自动选中的问题（头像加载时替换集合对象导致选中项引用失效）
- 主页账号选择框选中项缺少默认图标显示（无头像时出现空白头像框）
- 缓存系统添加时间戳和过期检查，磁盘缓存默认30分钟过期，避免加载陈旧数据
- 资源列表类型切换后无法及时更新列表内容的问题，修复IsViewReady未初始化的bug
- 资源列表加载时结果区域未隐藏导致空列表和骨架屏同时显示
- 版本过滤器切换后不触发搜索
- 浅色模式导航栏hover背景色与白色背景几乎无区别的问题，添加专用NavHoverBrush资源
- 导航栏折叠时图标对齐突变，改为Grid列布局实现自然过渡
- 导航栏折叠按钮改为箭头翻转动画，替代之前的汉堡菜单旋转

## [v1.0.0-rc.2] - 2025-03-13

### 新增
- 插件系统支持主页卡片注册
- 设置页面支持插件卡片管理

### 修复
- 修复插件卡片在设置中无法显示的问题
- 修复插件卡片加载限制问题

### 变更
- 优化插件卡片刷新逻辑

## [v1.0.0-rc.1] - 2025-03-12

### 新增
- 初始发布版本
- 基础启动器功能
- Minecraft 版本下载与管理
- 账号管理（离线、微软、外置登录）
- 模组/资源包下载（Modrinth、CurseForge）
- 多人联机服务器管理
- 插件系统基础框架