using Xunit;

// PluginContext 依赖大量静态回调与静态状态（OnTabRegistered / OnTabRegisteredWithContent /
// OnTabUnregistered / OnLogMessage / OnGetInstalledVersions / OnGetCurrentAccount /
// OnRequestDownload、事件处理器表、启动钩子表、命令表等），多个测试类共享这些静态成员。
// xUnit 默认按测试类并行执行，并行类之间会互相覆盖静态回调，导致偶发失败
// （如 PluginPageFlowTests.Integration_FullPageLifecycle_RegisterEditDelete、
// PluginTabCustomContentTests.RegisterTab_MixedRegistration_BothCallbacksInvoked）。
// 整个程序集禁用并行执行，保证测试确定性（本套件约 320 个测试，串行耗时 <1s）。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
