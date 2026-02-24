using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ObsMCLauncher.Desktop.ViewModels.Dialogs;

public enum DialogType
{
    Info,
    Success,
    Warning,
    Error,
    Question,
    Input
}

public enum DialogButtons
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum DialogResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public sealed partial class DialogRequest : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string InputText { get; set; } = string.Empty;

    public string Placeholder { get; init; } = string.Empty;

    public DialogType Type { get; init; }

    public DialogButtons Buttons { get; init; }

    public TaskCompletionSource<(DialogResult Result, string Text)> Completion { get; } = new();

    [ObservableProperty]
    private double _animationOpacity = 0;

    [ObservableProperty]
    private double _animationScale = 0.5;

    [ObservableProperty]
    private double _animationOffsetY = -40;

    private Timer? _animationTimer;
    private int _animationStep = 0;
    private static readonly int TotalSteps = 20;
    private static readonly int EnterDuration = 400;

    public void StartEnterAnimation()
    {
        _animationStep = 0;
        _animationTimer?.Dispose();
        _animationTimer = new Timer(AnimateEnterStep, null, 0, EnterDuration / TotalSteps);
    }

    private void AnimateEnterStep(object? state)
    {
        if (_animationStep > TotalSteps)
        {
            _animationTimer?.Dispose();
            return;
        }

        var progress = (double)_animationStep / TotalSteps;
        var easedProgress = EaseOutBack(progress);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AnimationOpacity = Math.Min(1, progress * 2);
            AnimationScale = 0.5 + 0.5 * easedProgress;
            AnimationOffsetY = -40 + 40 * EaseOutCubic(progress);
            _animationStep++;
        });
    }

    public void StartExitAnimation()
    {
        _animationTimer?.Dispose();
        _animationStep = 0;
        _animationTimer = new Timer(AnimateExitStep, null, 0, 15);
    }

    private void AnimateExitStep(object? state)
    {
        if (_animationStep > 10)
        {
            _animationTimer?.Dispose();
            return;
        }

        var progress = (double)_animationStep / 10;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AnimationOpacity = 1 - progress;
            AnimationScale = 1 - 0.2 * progress;
            AnimationOffsetY = -20 * progress;
            _animationStep++;
        });
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private static double EaseOutCubic(double t)
    {
        return 1 - Math.Pow(1 - t, 3);
    }
}

public sealed partial class UpdateDialogRequest : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString();

    public string Title { get; init; } = string.Empty;

    public string MarkdownContent { get; init; } = string.Empty;

    public string ConfirmText { get; init; } = "确定";

    public string CancelText { get; init; } = "取消";

    public TaskCompletionSource<bool> Completion { get; } = new();

    [ObservableProperty]
    private double _animationOpacity = 0;

    [ObservableProperty]
    private double _animationScale = 0.5;

    [ObservableProperty]
    private double _animationOffsetY = -40;

    private Timer? _animationTimer;
    private int _animationStep = 0;
    private static readonly int TotalSteps = 20;
    private static readonly int EnterDuration = 400;

    public void StartEnterAnimation()
    {
        _animationStep = 0;
        _animationTimer?.Dispose();
        _animationTimer = new Timer(AnimateEnterStep, null, 0, EnterDuration / TotalSteps);
    }

    private void AnimateEnterStep(object? state)
    {
        if (_animationStep > TotalSteps)
        {
            _animationTimer?.Dispose();
            return;
        }

        var progress = (double)_animationStep / TotalSteps;
        var easedProgress = EaseOutBack(progress);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AnimationOpacity = Math.Min(1, progress * 2);
            AnimationScale = 0.5 + 0.5 * easedProgress;
            AnimationOffsetY = -40 + 40 * EaseOutCubic(progress);
            _animationStep++;
        });
    }

    public void StartExitAnimation()
    {
        _animationTimer?.Dispose();
        _animationStep = 0;
        _animationTimer = new Timer(AnimateExitStep, null, 0, 15);
    }

    private void AnimateExitStep(object? state)
    {
        if (_animationStep > 10)
        {
            _animationTimer?.Dispose();
            return;
        }

        var progress = (double)_animationStep / 10;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AnimationOpacity = 1 - progress;
            AnimationScale = 1 - 0.2 * progress;
            AnimationOffsetY = -20 * progress;
            _animationStep++;
        });
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private static double EaseOutCubic(double t)
    {
        return 1 - Math.Pow(1 - t, 3);
    }
}
