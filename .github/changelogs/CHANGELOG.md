 ## [v1.0.0-rc.5] - 2026-06-xx

 ### 优化
- 侧边栏导航重构为 SplitView 组件，使用原生动画替代手动宽度动画，提升流畅度和性能
- 侧边栏支持基于窗口宽度的自适应折叠（宽度 < 950px 自动折叠，>= 950px 自动展开），手动切换后保持用户偏好
- 游戏目录列表新增两个不可删除的默认目录：官方目录（平台自适应路径）和启动器目录（启动器根目录\\.minecraft），置顶显示并带有"默认"标识
- 资源详情页前置资源优化：同一MC版本下的公共前置资源提升至版本分组顶部显示，避免重复；无公共前置时保持原布局
- 修复前置资源名称解析失败显示"Mod #xxxx"的问题，批量获取失败后自动逐个回退获取
- 修复资源详情页筛选切换时版本组重复渲染及交互失效的问题
- 资源详情页加载器图标改用内置 PNG 图标，新增加载器快速筛选栏
- 侧边栏导航图标改用 SVG 矢量图标替代 Emoji，提升跨平台渲染一致性；导航图标文件统一管理在 Assets/SidebarIcons 目录，文件名为 dashboard/multiplayer/accounts/versions/resources/settings
- 修复 SVG 图标颜色跟随主题色问题，BitmapAssetValueConverter 新增 SVG 支持并载入时自动替换 currentColor 为当前 TextBrush 颜色
- 修复导航项 SVG 图标与 Emoji 回退同时显示的重叠问题（NotConverter → NullToBoolConverter Invert）