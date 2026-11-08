using ObsMCLauncher.Core.Plugins;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 启动进度换算：把启动流程里的各种回调折算成 0-100 的整体进度。
/// 只做计算，不碰 UI，回调从哪个线程来都行（值本身用锁保护）。
/// </summary>
public class LaunchProgressTracker
{
    // 阶段划分。前后咬合，避免出现空档
    public const int StageIntegrity = 0;
    public const int StageMissingDependencies = 1;
    public const int StageConflictCheck = 2;
    public const int StagePrepare = 3;
    public const int StageInternalLibraries = 4;
    public const int StageAssets = 5;
    public const int StagePluginSteps = 6;
    public const int StageStartProcess = 7;
    public const int StageDone = 8;

    private static readonly double[] StageStart = { 0, 5, 20, 22, 28, 40, 86, 96, 100 };
    private static readonly double[] StageEnd = { 5, 20, 22, 28, 40, 86, 96, 99, 100 };

    private readonly object _lock = new();
    private int _stage = StageIntegrity;
    private double _target;

    /// <summary>当前应该到达的进度上限（0-100）</summary>
    public double TargetPercent
    {
        get { lock (_lock) { return _target; } }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _stage = StageIntegrity;
            _target = 0;
        }
    }

    /// <summary>按消息内容自动识别阶段</summary>
    public void Report(string? message)
    {
        var stage = ResolveStage(message);
        if (stage < 0) return;
        ReportPhase(stage, message, null);
    }

    /// <summary>
    /// 上报某个阶段的进度。percent 为 null 表示该阶段没有真实进度，
    /// 目标值停在区间末端之前，由界面定时器慢慢爬过去。
    /// </summary>
    public void ReportPhase(int stage, string? message, double? percent)
    {
        lock (_lock)
        {
            if (stage < StageIntegrity) stage = StageIntegrity;
            if (stage > StageDone) stage = StageDone;
            // 阶段只向前推进。同一个文案在多个阶段出现时，靠这一点避免进度倒退
            if (stage > _stage) _stage = stage;

            var start = StageStart[_stage];
            var end = StageEnd[_stage];

            double target;
            if (_stage == StageDone)
            {
                target = 100;
            }
            else if (percent != null)
            {
                var p = percent.Value;
                if (p < 0) p = 0;
                if (p > 100) p = 100;
                target = start + (end - start) * p / 100.0;
                // 真实百分比也留一点余量，最后一格由 Complete 补
                target = System.Math.Min(target, end - 0.3);
            }
            else
            {
                target = end - 0.8;
            }

            if (target > _target) _target = target;
        }
    }

    /// <summary>插件启动步骤：整体占 86-96 这段，按权重插值</summary>
    public void ReportStep(LaunchStepProgress progress)
    {
        if (progress == null) return;

        var total = progress.TotalWeight;
        if (total <= 0) total = 1;

        var done = progress.CompletedWeight + progress.Weight * progress.StepPercent / 100.0;
        var percent = 86 + done / total * 10;
        if (percent < 86) percent = 86;
        if (percent > 96) percent = 96;

        ReportPhase(StagePluginSteps, progress.DisplayName, (percent - 86) / 10 * 100);
    }

    /// <summary>启动流程走完，进度拉满</summary>
    public void Complete()
    {
        ReportPhase(StageDone, null, 100);
    }

    /// <summary>去掉回调里附带的技术后缀（"(45%)|45" 这类），只留给人看的文字</summary>
    public static string CleanMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var text = message;
        var barIndex = text.LastIndexOf('|');
        if (barIndex >= 0) text = text.Substring(0, barIndex);

        // 资源下载回调的 "(45%)" 尾巴
        var closing = text.LastIndexOf(')');
        if (closing == text.Length - 1)
        {
            var opening = text.LastIndexOf('(', closing);
            if (opening >= 0)
            {
                var inside = text.Substring(opening + 1, closing - opening - 1);
                if (inside.EndsWith("%"))
                {
                    text = text.Substring(0, opening);
                }
            }
        }

        return text.Trim();
    }

    private static int ResolveStage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return -1;
        var msg = message;

        if (msg.Contains("完整性")) return StageIntegrity;
        if (msg.Contains("模组冲突")) return StageConflictCheck;
        if (msg.Contains("缺失依赖")) return StageMissingDependencies;
        if (msg.Contains("启动参数") || msg.Contains("启动游戏进程")) return StageStartProcess;
        if (msg.Contains("外置登录") || msg.Contains("令牌") || msg.Contains("游戏版本信息")
            || msg.Contains("解压本地库") || msg.Contains("客户端文件")) return StagePrepare;
        if (msg.Contains("缺失的库文件") || msg.Contains("可选库文件")) return StageInternalLibraries;
        if (msg.Contains("游戏资源")) return StageAssets;
        if (msg.Contains("启动完成")) return StageDone;

        // 这些词前后两个阶段都会出现，映射到最早的阶段；靠阶段只进不退兜底
        if (msg.Contains("版本信息") || msg.Contains("Java环境") || msg.Contains("主文件")
            || msg.Contains("依赖库")) return StageIntegrity;

        return -1;
    }
}
