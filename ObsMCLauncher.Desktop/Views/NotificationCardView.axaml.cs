using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Desktop.ViewModels.Notifications;

namespace ObsMCLauncher.Desktop.Views;

/// <summary>
/// 通知卡片视图：InfoBar + 进度条。
/// 右下角模式支持右滑关闭：拖拽期间临时禁用过渡实现跟手，释放后由过渡动画回弹或退出。
/// 悬停时置 IsPaused，暂停自动关闭倒计时。
/// </summary>
public partial class NotificationCardView : UserControl
{
    private Point _swipeStart;
    private bool _swipeActive;
    private bool _transitionsSuppressed;
    private Transitions? _savedTransitions;

    public NotificationCardView()
    {
        InitializeComponent();
    }

    private NotificationItemViewModel? Vm => DataContext as NotificationItemViewModel;

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (Vm != null) Vm.IsPaused = true;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (Vm != null) Vm.IsPaused = false;
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        // 右下角模式：点击任意处关闭（InfoBar 自带关闭按钮，此处保持原有习惯）
        if (Vm?.Position == NotificationPosition.BottomRight)
        {
            Vm.CloseCommand.Execute(null);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm?.Position != NotificationPosition.BottomRight) return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        _swipeStart = point.Position;
        _swipeActive = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_swipeActive || Vm?.Position != NotificationPosition.BottomRight) return;

        var deltaX = e.GetPosition(this).X - _swipeStart.X;
        if (deltaX > 5)
        {
            SuppressTransitions();
            Vm.UpdateSwipeDrag(deltaX);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_swipeActive) return;

        _swipeActive = false;
        RestoreTransitions();

        var totalDeltaX = e.GetPosition(this).X - _swipeStart.X;
        Vm?.EndSwipeDrag(totalDeltaX);
    }

    private void SuppressTransitions()
    {
        if (_transitionsSuppressed) return;

        _savedTransitions = RootCard.Transitions;
        RootCard.Transitions = null;
        _transitionsSuppressed = true;
    }

    private void RestoreTransitions()
    {
        if (!_transitionsSuppressed) return;

        RootCard.Transitions = _savedTransitions;
        _transitionsSuppressed = false;
    }
}
