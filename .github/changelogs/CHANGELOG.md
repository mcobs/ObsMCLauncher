## [v1.1.0-beta.1] - 2026-08-14

### 修复
- 修复更新服务无法检测不同通道更新（正式版/测试版/预发布版/预览版）的问题：客户端请求的 Velopack 通道名缺少 RID 前缀，与发布端生成的 feed 文件名（如 releases.win-x64-pre.json）不匹配，导致检查更新永远返回"已是最新版本"
- 修复分组管理弹窗（ContentDialog）内绑定错误：弹窗内容在弹出层中渲染丢失 DataContext 继承，改为显示前显式绑定到当前版本实例 ViewModel

### 优化
- 版本实例界面迁移到 FluentAvalonia 组件：顶部导航标签栏改用 TabView（图标改用 PathIconSource），内存配置输入框改用 NumberBox，分组管理弹窗改用 ContentDialog，OptiFine 兼容性警告改用 InfoBar
- 版本实例页标签栏禁止拖动与重排序，避免误操作改变标签顺序
- 版本实例界面统一危险色语义：硬编码红色（#FF5252/#E74C3C 等）改用主题 DangerBrush / DangerSoftBrush 资源
- 版本实例页图标缓存改用稳定哈希（SHA256）生成文件名，修复 string.GetHashCode 跨进程随机化导致缓存失效、每次打开实例都重新解压图标的问题
- 版本实例页分组管理入口改为独立按钮，不再隐藏在分组下拉框中
- 内存配置新增联动提示：最小内存不小于最大内存或超过最大内存 1/4 时显示警告
- 版本实例页加载态改用骨架屏占位，替代全屏遮罩
- 设置页全局内存配置新增联动提示：最大内存不大于最小内存时显示警告

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
