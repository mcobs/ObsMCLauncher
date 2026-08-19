## [v1.1.0-beta.4] - 2026-08-19

### 修复
- 修复插件扩展 API 在生产环境失效的问题：`LogMessage`、`GetInstalledVersions`、`GetCurrentAccount` 此前未在 Desktop 层接线，现真正生效；`RequestDownload` 已接入下载管理器，并校验目标目录白名单与 SHA-1
- 修复游戏启动生命周期钩子从未触发的问题：启动钩子已接入 `GameLauncher` 启动流程（BeforeLaunch 可追加参数/拦截，AfterLaunch、退出/崩溃阶段均回调），并补发 `GameLaunched` / `GameClosed` 事件
- 补发此前未发布的 `VersionDownloaded` 事件（版本下载完成时触发）
- 修复卸载/禁用插件未清理静态状态的问题：卸载/禁用时同步清理该插件的启动钩子与事件订阅
- 修复带自定义 UI 的插件标签页插件 ID 被硬编码导致卸载后残留的问题，现按真实插件 ID 注册与清理
- 插件事件系统新增 `UnsubscribeEvent` 退订能力，插件可主动退订事件
- 插件元数据校验：校验 plugin.json 的 id 格式、目录名与 id 一致、id 唯一、依赖插件存在及最低启动器版本要求，校验失败自动禁用并提示
- `LauncherVersion` 改用真实版本源（同步启动器实际版本，不再硬编码）
- 市场安装插件后改为增量加载单插件，避免全量重扫导致已加载插件被重复初始化

### 新增
- 新增插件通用配置读写 API：`GetConfig<T>` / `SaveConfig<T>`（存于插件数据目录 config.json）
- 新增 `OpenUrl` 打开外部链接、`NavigateTo` 跳转启动器内部页面的 API
- 新增 `GetDownloadTaskStatus` 查询下载任务状态的 API
- 新增异步游戏启动生命周期钩子 `RegisterGameLaunchHookAsync`

## [v1.1.0-beta.3] - 2026-08-16

### 优化
- 设置项尽量用分段选择替代下拉框（更新通道、主题模式、密度、动画级别、显示方式、版本隔离、下载源、镜像源、下载线程等）
- 完善 PCL/HMCL 数据迁移：PCL 补齐游戏目录（含附加目录列表）、JVM 参数、自定义内存等设置的读取；HMCL 补齐全局游戏设置预设、用户级游戏目录与实例级自定义 Java 的迁移
- 迁移完成页新增"查看迁移详情"条目，可逐项查看导入/跳过/警告的具体内容
