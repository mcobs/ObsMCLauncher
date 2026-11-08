using System;
using System.Collections.Generic;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件日志级别
/// </summary>
public enum PluginLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// 已安装版本精简信息（仅暴露插件需要的安全字段）
/// </summary>
public class PluginVersionInfo
{
    public string VersionId { get; set; } = string.Empty;

    public string McVersion { get; set; } = string.Empty;

    /// <summary>加载器类型：vanilla/forge/fabric/quilt/neoforge/optifine</summary>
    public string LoaderType { get; set; } = "vanilla";

    public string VersionDirectory { get; set; } = string.Empty;

    public DateTime? LastPlayed { get; set; }
}

/// <summary>
/// 当前账户的精简信息（不含任何令牌/敏感字段）
/// </summary>
public class PluginAccountInfo
{
    public string AccountId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>账户类型：Offline/Microsoft/Yggdrasil</summary>
    public string AccountType { get; set; } = string.Empty;

    public string UUID { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
}

/// <summary>
/// 游戏启动生命周期钩子触发阶段
/// </summary>
public enum GameLaunchPhase
{
    /// <summary>启动前（可在此修改参数或拦截启动）</summary>
    BeforeLaunch,

    /// <summary>游戏进程已启动</summary>
    AfterLaunch,

    /// <summary>游戏进程退出</summary>
    OnExited,

    /// <summary>检测到崩溃</summary>
    OnCrash
}

/// <summary>
/// 启动钩子上下文，随阶段不同字段含义不同
/// </summary>
public class GameLaunchHookContext
{
    public string VersionId { get; set; } = string.Empty;

    public string McVersion { get; set; } = string.Empty;

    public string GameDirectory { get; set; } = string.Empty;

    public string JavaPath { get; set; } = string.Empty;

    /// <summary>仅 OnExited/OnCrash 阶段有效；正常退出为 0</summary>
    public int ExitCode { get; set; }

    /// <summary>仅 OnCrash 阶段有效</summary>
    public string? CrashReport { get; set; }

    /// <summary>BeforeLaunch 阶段设为 true 可中止启动</summary>
    public bool CancelLaunch { get; set; }

    /// <summary>BeforeLaunch 阶段可追加额外 JVM 参数</summary>
    public List<string> ExtraJvmArguments { get; } = new();

    /// <summary>BeforeLaunch 阶段可追加额外游戏参数</summary>
    public List<string> ExtraGameArguments { get; } = new();
}

/// <summary>
/// 插件启动步骤的执行状态
/// </summary>
public enum LaunchStepPhase
{
    /// <summary>等待执行</summary>
    Waiting,

    /// <summary>正在执行</summary>
    Running,

    /// <summary>执行完毕</summary>
    Completed,

    /// <summary>被插件主动跳过</summary>
    Skipped,

    /// <summary>执行出错（不会中断启动，进度按该步骤已完成的权重继续）</summary>
    Failed,

    /// <summary>被取消（启动流程随即中止）</summary>
    Cancelled
}

/// <summary>
/// 插件注册的一个启动步骤。
///
/// 步骤在启动器完成游戏文件准备（完整性校验、补缺依赖、资源校验）之后、
/// 构建启动参数并拉起进程之前执行，排在所有内置阶段后面。
/// </summary>
public class PluginLaunchStep
{
    /// <summary>步骤标识（插件内唯一，不要带插件前缀，启动器会自己加）</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>显示名称，会出现在启动遮罩的状态行上</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 权重，决定这一步分到多少进度区间。默认 1，必须大于 0。
    /// 插件步骤整体只占整个启动进度的 86% - 96%，权重按比例分配这 10 个百分点。
    /// </summary>
    public double Weight { get; set; } = 1;

    /// <summary>执行体；抛异常或返回的任务失败都算 Failed，启动会继续</summary>
    public Func<PluginLaunchStepContext, Task>? ExecuteAsync { get; set; }
}

/// <summary>
/// 启动步骤上下文
/// </summary>
public class PluginLaunchStepContext
{
    public string VersionId { get; set; } = string.Empty;

    public string McVersion { get; set; } = string.Empty;

    public string GameDirectory { get; set; } = string.Empty;

    public string JavaPath { get; set; } = string.Empty;

    /// <summary>设为 true 会中止本次启动，遮罩上显示"已被插件步骤取消"</summary>
    public bool CancelLaunch { get; set; }

    /// <summary>当前步骤的进度上报入口；由启动器注入</summary>
    internal Action<double, string?>? ProgressCallback { get; set; }

    /// <summary>
    /// 上报当前步骤的进度（0-100）。可以多次调用，启动器会把它映射到该步骤的权重区间。
    /// </summary>
    /// <param name="percent">0-100，超出范围会被夹紧</param>
    /// <param name="message">可选状态文字，显示在遮罩状态行上</param>
    public void ReportProgress(double percent, string? message = null)
    {
        ProgressCallback?.Invoke(percent, message);
    }

    /// <summary>
    /// 只更新状态文字，不动进度
    /// </summary>
    public void ReportStatus(string message)
    {
        ProgressCallback?.Invoke(double.NaN, message);
    }
}

/// <summary>
/// 启动步骤的进度快照。
///
/// <see cref="CompletedWeight"/> 是当前步骤之前已经完成的权重之和（失败/跳过的步骤按完成计），
/// 加上 <see cref="Weight"/> * <see cref="StepPercent"/> / 100 就是当前的整体已完成权重。
/// </summary>
public class LaunchStepProgress
{
    public string StepId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public LaunchStepPhase Phase { get; set; } = LaunchStepPhase.Waiting;

    /// <summary>当前步骤序号，从 0 开始</summary>
    public int Index { get; set; }

    /// <summary>步骤总数</summary>
    public int Total { get; set; }

    public double Weight { get; set; }

    /// <summary>当前步骤之前已完成的权重</summary>
    public double CompletedWeight { get; set; }

    /// <summary>全部步骤的权重之和</summary>
    public double TotalWeight { get; set; }

    /// <summary>当前步骤自身的进度（0-100）</summary>
    public double StepPercent { get; set; }

    /// <summary>插件上报的状态文字；没上报时为 null</summary>
    public string? Message { get; set; }
}

/// <summary>
/// 一轮启动步骤跑完的结果
/// </summary>
public class LaunchStepResult
{
    /// <summary>是否有步骤要求中止启动</summary>
    public bool Cancelled { get; set; }

    /// <summary>取消时最后收到的状态文字</summary>
    public string? CancelMessage { get; set; }

    /// <summary>本轮登记的步骤数（含没有执行体的空步骤）</summary>
    public int StepCount { get; set; }
}

/// <summary>
/// 插件提交的下载请求
/// </summary>
public class PluginDownloadRequest
{
    /// <summary>下载 URL（http/https）</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>下载任务显示名称</summary>
    public string TaskName { get; set; } = string.Empty;

    /// <summary>目标保存目录（启动器会校验是否在允许范围内）</summary>
    public string TargetDirectory { get; set; } = string.Empty;

    /// <summary>保存文件名（不含路径）</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>可选：SHA-1 校验值</summary>
    public string? Sha1 { get; set; }

    /// <summary>是否立即开始下载，false 表示仅创建任务</summary>
    public bool AutoStart { get; set; } = true;
}

/// <summary>
/// 下载任务的精简状态（供插件查询 RequestDownload 返回的任务进度）
/// </summary>
public class PluginDownloadTaskStatus
{
    public string TaskId { get; set; } = string.Empty;

    /// <summary>任务显示名称</summary>
    public string TaskName { get; set; } = string.Empty;

    /// <summary>任务类型：Version / Assets / Mod / Resource 等</summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>状态：Downloading / Completed / Failed / Cancelled；未知返回 Unknown</summary>
    public string Status { get; set; } = "Unknown";

    /// <summary>进度（0-100）</summary>
    public double Progress { get; set; }

    /// <summary>状态消息（失败时通常为错误信息）</summary>
    public string? StatusMessage { get; set; }
}

/// <summary>
/// 游戏进程运行状态快照
/// </summary>
public class PluginGameStatus
{
    /// <summary>游戏进程是否正在运行</summary>
    public bool IsRunning { get; set; }

    /// <summary>运行中的版本ID；未运行时为空</summary>
    public string VersionId { get; set; } = string.Empty;

    /// <summary>运行中的 Minecraft 版本号；未运行时为空</summary>
    public string McVersion { get; set; } = string.Empty;

    /// <summary>游戏进程 PID；未运行时为 0</summary>
    public int ProcessId { get; set; }

    /// <summary>进程启动时间；未运行时为 null</summary>
    public DateTime? StartedAt { get; set; }
}

/// <summary>
/// 启动设置快照（只读；改设置请引导用户去设置页，插件不提供写入口）
/// </summary>
public class PluginLaunchSettings
{
    public int MaxMemoryMb { get; set; }

    public int MinMemoryMb { get; set; }

    /// <summary>附加 JVM 参数（原始字符串）</summary>
    public string JvmArguments { get; set; } = string.Empty;

    /// <summary>当前生效的 Java 路径（按设置里的 Java 选择模式解析后）</summary>
    public string JavaPath { get; set; } = string.Empty;

    public string GameDirectory { get; set; } = string.Empty;

    /// <summary>启动游戏后是否关闭启动器</summary>
    public bool CloseAfterLaunch { get; set; }
}

/// <summary>
/// 选中版本 mods 目录里的一个模组文件（.jar 与 .jar.disabled 都会被列出来，靠 <see cref="IsEnabled"/> 区分）
/// </summary>
public class PluginModInfo
{
    /// <summary>模组 ID（来自模组元数据，如 fabric.mod.json / mods.toml）；解析不到时为空</summary>
    public string ModId { get; set; } = string.Empty;

    /// <summary>显示名称（元数据里的 name）；解析不到时退回文件名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模组版本（元数据里的 version）；解析不到时为空</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>加载器标识（元数据里的 loader，如 Fabric/Forge）；模组没写时用所属版本的加载器</summary>
    public string Loader { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>是否启用（未被 .disabled 后缀禁用）</summary>
    public bool IsEnabled { get; set; }

    /// <summary>模组图标缓存文件路径；没有图标时为 null</summary>
    public string? IconPath { get; set; }
}

/// <summary>
/// 选中版本 saves 目录里的一个存档（世界）
/// </summary>
public class PluginWorldInfo
{
    /// <summary>存档文件夹名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>存档文件夹完整路径</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>存档记录的游戏版本（读自 level.dat）；解析不到时为空</summary>
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>存档占用空间（递归统计整个文件夹）</summary>
    public long SizeBytes { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime LastModified { get; set; }

    /// <summary>存档图标（存档目录下的 icon.png）；没有时为 null</summary>
    public string? IconPath { get; set; }
}

/// <summary>
/// 选中版本 resourcepacks 目录里的一个材质包（.zip 与 .zip.disabled 都会被列出来）
/// </summary>
public class PluginResourcePackInfo
{
    /// <summary>启用时为去掉扩展名的文件名，禁用时为完整文件名（含 .disabled）</summary>
    public string Name { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>材质包图标缓存文件路径；没有图标时为 null</summary>
    public string? IconPath { get; set; }
}

/// <summary>
/// 选中版本 shaderpacks 目录里的一个光影包（.zip 与 .zip.disabled 都会被列出来）
/// </summary>
public class PluginShaderPackInfo
{
    /// <summary>启用时为去掉扩展名的文件名，禁用时为完整文件名（含 .disabled）</summary>
    public string Name { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>光影包图标缓存文件路径；没有图标时为 null</summary>
    public string? IconPath { get; set; }
}
