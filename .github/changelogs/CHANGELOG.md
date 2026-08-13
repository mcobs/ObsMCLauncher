## [Unreleased] - 2026-08-13

### 修复
- 修复"更多-插件"详情 README 加载竞态：快速切换插件时，旧请求结果不再覆盖当前选中插件的内容
- 修复"更多-截图管理"分页加载失效的问题：滚动到底部时自动加载更多截图，此前最多只显示前 20 张
- 修复浅色主题下导航栏/标题栏/窗口/卡片背景仍为深色的问题：ApplyLightTheme/ApplyDarkTheme 补充 NavBackgroundBrush、TitleBarBackgroundBrush、WindowBackgroundBrush、CardBackgroundBrush 等资源更新
- 修复资源中心搜索结果已显示但骨架屏仍不消失的问题：Modrinth 版本信息改为后台异步加载，不再阻塞搜索完成；搜索增加 60 秒超时兜底；已有结果时立即隐藏骨架屏
- 导航栏图标改用 PathIconSource + SvgToGeometryConverter，图标随 Foreground 变色（选中态变主题色、主题切换跟随文本色），不再使用固定颜色的 ImageIconSource
- SvgThemeHelper 浅色/深色图标颜色改为 TextSecondaryBrush 色值，避免纯黑/纯白对比度过强

### 优化
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

### 修复
- 修复主页卡片导航到"设置/更多"页无效的问题（底部导航项未参与查找）

## [v1.0.1-beta.2] - 2026-08-12

### 修复
- 修复安装原版版本时未下载版本文件导致资源补全失败的问题
- 修复下载管理器无法显示补全资源任务的进度详情的问题

### 优化
- 资源详情页返回按钮移至页面左上角独立一行，不再与资源标题挤在同一行
