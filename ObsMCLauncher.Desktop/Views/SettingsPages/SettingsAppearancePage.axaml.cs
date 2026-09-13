using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views.SettingsPages;

public partial class SettingsAppearancePage : UserControl
{
    /// <summary>
    /// 拖拽负载格式：应用私有的字符串格式（标识符不会下发给平台，因此不会与外部拖入的文件混淆）。
    /// 传的是<b>卡片路径</b>而不是卡片对象本身——视图只负责搬运"是哪一张"，
    /// 由 ViewModel 决定它此刻在列表里的位置，避免把对象引用跨层塞进拖拽负载。
    /// </summary>
    private static readonly DataFormat<string> CardDragFormat =
        DataFormat.CreateStringApplicationFormat("ObsMCLauncher.WallpaperCard");

    /// <summary>
    /// 起拖阈值（逻辑像素）。低于它的移动算点击——否则手指稍微抖一下就把"点卡片"变成"拖动卡片"，
    /// 悬停操作组里的三个按钮会变得很难点中。
    /// </summary>
    private const double DragThreshold = 6;

    /// <summary>本次按压可能是一次拖拽的起点（尚未越过阈值）；<c>null</c> 表示不在候选状态</summary>
    private WallpaperItemViewModel? _dragSource;

    /// <summary>按下时的坐标，用来量位移是否越过 <see cref="DragThreshold"/></summary>
    private Point _pressPosition;

    public SettingsAppearancePage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 拒收提示条上的关闭按钮。InfoBar 会先把自身的 <c>IsOpen</c> 置假，
    /// 但那个状态是"这一帧的显示与否"，与"这件事还要不要提醒"是两回事——
    /// 因此这里回写 ViewModel，让它把消息一起清掉（下次再有文件被拒收会重新出现）。
    /// </summary>
    private void OnWallpaperRejectionBarClosed(InfoBar sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.DismissWallpaperRejectionCommand.Execute(null);
        }
    }

    // ===== 拖拽排序（§8.5）=====

    /// <summary>
    /// 按下只记位置，<b>不立刻起拖</b>。真正的起拖交给
    /// <see cref="OnWallpaperCardPointerMoved"/> 判定阈值，这样卡片上的三个按钮仍能被正常点击。
    /// </summary>
    private void OnWallpaperCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 按压落在操作按钮上时这次交互归按钮所有，别抢它的指针
        if (e.Source is Control source && source.FindAncestorOfType<Button>() is not null)
        {
            _dragSource = null;
            return;
        }

        if (sender is not Control card || card.DataContext is not WallpaperItemViewModel item) return;
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;

        _dragSource = item;
        _pressPosition = e.GetPosition(card);
    }

    private async void OnWallpaperCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is not { } item || sender is not Control card) return;

        // 中途松开左键就当作一次点击，不再起拖
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
        {
            _dragSource = null;
            return;
        }

        var position = e.GetPosition(card);
        if (Math.Abs(position.X - _pressPosition.X) < DragThreshold &&
            Math.Abs(position.Y - _pressPosition.Y) < DragThreshold)
        {
            return;
        }

        // 越过阈值：交给系统的拖拽循环接管指针，此后本次按压不可能再被解释成点击
        _dragSource = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(CardDragFormat, item.Path));

        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            // 拖拽是纯 UI 增强，任何平台侧的意外都不该把设置页带崩
            System.Diagnostics.Debug.WriteLine($"[Wallpaper] 拖拽失败: {ex.Message}");
        }
        finally
        {
            ClearDropTarget();
        }
    }

    private void OnWallpaperCardPointerReleased(object? sender, PointerReleasedEventArgs e)
        => _dragSource = null;

    private void OnWallpaperCardDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(CardDragFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;

        if (sender is Control card && card.DataContext is WallpaperItemViewModel item &&
            DataContext is SettingsViewModel vm)
        {
            vm.SetWallpaperDropTarget(item);
        }
    }

    private void OnWallpaperCardDragLeave(object? sender, DragEventArgs e) => ClearDropTarget();

    private void OnWallpaperCardDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control card || card.DataContext is not WallpaperItemViewModel target) return;
        if (ResolveDraggedCard(e) is not { } source) return;
        if (DataContext is not SettingsViewModel vm) return;

        e.Handled = true;
        vm.SetWallpaperDropTarget(null);
        vm.MoveWallpaperTo(source, target);
    }

    /// <summary>落在"添加"卡上 = 挪到列表末尾（往卡片区外面甩出去的自然落点）</summary>
    private void OnAddCardDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(CardDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        // 指针已离开卡片区，卡片上的落点高亮要跟着灭
        ClearDropTarget();
    }

    private void OnAddCardDrop(object? sender, DragEventArgs e)
    {
        if (ResolveDraggedCard(e) is not { } source) return;
        if (DataContext is not SettingsViewModel vm) return;

        e.Handled = true;
        vm.SetWallpaperDropTarget(null);
        vm.MoveWallpaperToEnd(source);
    }

    /// <summary>从拖拽负载里取出源卡片：负载是路径，回查交给 ViewModel（路径在列表内唯一，添加阶段已去重）</summary>
    private WallpaperItemViewModel? ResolveDraggedCard(DragEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return null;

        var path = e.DataTransfer.TryGetValue(CardDragFormat);
        return string.IsNullOrEmpty(path) ? null : vm.FindWallpaperCard(path);
    }

    private void ClearDropTarget()
    {
        if (DataContext is SettingsViewModel vm) vm.SetWallpaperDropTarget(null);
    }
}
