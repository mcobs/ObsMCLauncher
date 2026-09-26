# Changelog

## [unreleased] - 2026-xx-xx

### 新增


### 修复
- 【启动】修复明明有崩溃日志却显示「报告：<未找到>」的问题
- 【启动】修复带版本隔离的整合包（1.18.2 一类）启动报 ClassNotFoundException: org/lwjgl/system/Platform 的问题
- 【启动】修复 Fabric/Forge 依赖（如 ASM）启动报 “ASM not detected on the classpath” 的问题
- 【启动】修复带 `inheritsFrom` 的版本（整合包导入、Fabric/Forge 衍生版本）启动即退出、退出码 1 且没有崩溃报告的问题
- 【下载】修复从未下载过 natives 原生库（classifier）的问题

### 优化
