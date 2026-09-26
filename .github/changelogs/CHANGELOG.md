# Changelog

## [unreleased] - 2026-xx-xx

### 新增


### 修复
- 【界面】修复游戏日志量大时软件卡死（假死）的问题：以前每收到一行日志就往界面线程投递一次、并且超出上限后每行都触发一次全量重建；现在改为后台线程只入队、界面按固定节奏批量刷新，并给裁剪加了余量、给待处理队列加了上限
- 【启动】修复明明有崩溃日志却显示「报告：<未找到>」的问题
- 【启动】修复带版本隔离的整合包（1.18.2 一类）启动报 ClassNotFoundException: org/lwjgl/system/Platform 的问题
- 【启动】修复 Fabric/Forge 依赖（如 ASM）启动报 “ASM not detected on the classpath” 的问题
- 【启动】修复带 `inheritsFrom` 的版本（整合包导入、Fabric/Forge 衍生版本）启动即退出、退出码 1 且没有崩溃报告的问题
- 【下载】修复从未下载过 natives 原生库（classifier）的问题

### 优化
