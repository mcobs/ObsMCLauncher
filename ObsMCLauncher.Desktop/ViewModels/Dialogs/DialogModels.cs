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
    private double _animationScale = 0.7;

    [ObservableProperty]
    private double _animationOffsetY = -30;

    private Timer? _animationTimer;
    private int _animationStep = 0;
    private static readonly double[] ScaleSteps = { 0.7, 1.05, 0.95, 1.02, 1.0 };
    private static readonly double[] OffsetSteps = { -30, 5, -2, 1, 0 };
    private static readonly double[] OpacitySteps = { 0, 1, 1, 1, 1 };

    public void StartEnterAnimation()
    {
        _animationStep = 0;
        _animationTimer?.Dispose();
        _animationTimer = new Timer(AnimateStep, null, 0, 50);
    }

    private void AnimateStep(object? state)
    {
        if (_animationStep >= ScaleSteps.Length)
        {
            _animationTimer?.Dispose();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_animationStep < ScaleSteps.Length)
            {
                AnimationOpacity = OpacitySteps[_animationStep];
                AnimationScale = ScaleSteps[_animationStep];
                AnimationOffsetY = OffsetSteps[_animationStep];
                _animationStep++;
            }
        });
    }

    public void StartExitAnimation()
    {
        _animationTimer?.Dispose();
        AnimationOpacity = 0;
        AnimationScale = 0.9;
        AnimationOffsetY = -20;
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
    private double _animationScale = 0.7;

    [ObservableProperty]
    private double _animationOffsetY = -30;

    private Timer? _animationTimer;
    private int _animationStep = 0;
    private static readonly double[] ScaleSteps = { 0.7, 1.05, 0.95, 1.02, 1.0 };
    private static readonly double[] OffsetSteps = { -30, 5, -2, 1, 0 };
    private static readonly double[] OpacitySteps = { 0, 1, 1, 1, 1 };

    public void StartEnterAnimation()
    {
        _animationStep = 0;
        _animationTimer?.Dispose();
        _animationTimer = new Timer(AnimateStep, null, 0, 50);
    }

    private void AnimateStep(object? state)
    {
        if (_animationStep >= ScaleSteps.Length)
        {
            _animationTimer?.Dispose();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_animationStep < ScaleSteps.Length)
            {
                AnimationOpacity = OpacitySteps[_animationStep];
                AnimationScale = ScaleSteps[_animationStep];
                AnimationOffsetY = OffsetSteps[_animationStep];
                _animationStep++;
            }
        });
    }

    public void StartExitAnimation()
    {
        _animationTimer?.Dispose();
        AnimationOpacity = 0;
        AnimationScale = 0.9;
        AnimationOffsetY = -20;
    }
}
