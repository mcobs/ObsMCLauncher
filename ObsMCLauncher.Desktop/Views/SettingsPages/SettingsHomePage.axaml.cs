using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views.SettingsPages;

public partial class SettingsHomePage : UserControl
{
    // 行模板根节点的样式类名，命中测试按它找行
    private const string RowRootClass = "edit-row-root";

    // 超过这个距离才算拖拽，否则当作点击
    private const double DragThreshold = 4;

    // 拖拽自动滚动：指针靠近预览可视区上下边缘时触发，每帧滚动一个步长
    private const double AutoScrollEdge = 36;
    private const double AutoScrollStep = 18;
    private readonly DispatcherTimer _autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private int _autoScrollDir; // -1 向上、0 不动、1 向下

    private static readonly Cursor DragCursor = new(StandardCursorType.DragMove);

    private Point _pressPos;
    private object? _pendingDrag;

    // 拖拽期间有效：当前拖的是组件还是组件库条目（进程内直接传引用）
    private bool _isDragging;
    private HomeRowViewModel? _dropTargetRow;

    private SettingsHomeViewModel? Vm => DataContext as SettingsHomeViewModel;

    public SettingsHomePage()
    {
        InitializeComponent();
        // 捕获被系统拿走（窗口切换等）时收尾，避免卡在拖拽状态
        PointerCaptureLost += OnPointerCaptureLost;
        _autoScrollTimer.Tick += OnAutoScrollTick;
    }

    // 页面经 Frame 导航创建，DataContext 继承的是 SettingsViewModel，
    // 这里换成主页编辑器自己的视图模型。
    // 用 Dispatcher 延迟一帧，确保 XAML 绑定先用 SettingsViewModel 解析完，再切到 SettingsHomeViewModel
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel svm && svm.SettingsHome != null)
        {
            var homeVm = svm.SettingsHome;
            Dispatcher.UIThread.Post(() =>
            {
                // 再次检查，避免重复切换
                if (!ReferenceEquals(DataContext, homeVm))
                {
                    DataContext = homeVm;
                }
            }, DispatcherPriority.Loaded);
        }
    }

    // 组件外壳按下：立即捕获指针到页面，位移超阈值后转为拖拽。
    // 捕获能防止后续移动被预览的 ScrollViewer 抢走（之前拖不动的根源）。
    // 注意：这里不立即选中，拖拽过程中不点亮组件；纯点击（未拖动）释放时才选中
    private void ComponentChrome_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm == null || sender is not Border { DataContext: HomeComponentViewModel vm })
        {
            return;
        }
        _pendingDrag = vm;
        _pressPos = e.GetPosition(PageRoot);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    // 组件库按下：同样捕获，release 时按是否拖拽区分"点击添加"与"拖入预览"
    private void LibraryChip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: LibraryComponentItem item })
        {
            return;
        }
        _pendingDrag = item;
        _pressPos = e.GetPosition(PageRoot);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void AddRow_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        Vm.Home.InsertRow(Vm.Home.HomeRows.Count);
        // 等布局更新后滚到底部，让新行进入视野
        Dispatcher.UIThread.Post(PreviewScroll.ScrollToEnd, DispatcherPriority.Loaded);
    }

    // 添加行槽位：预览内容未滚动到底部时显示，到底后隐藏（避免贴着底部操作区显得多余）
    private void PreviewScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var sc = PreviewScroll;
        var overflowing = sc.Extent.Height > sc.Viewport.Height + 1;
        var atBottom = overflowing && sc.Offset.Y >= sc.Extent.Height - sc.Viewport.Height - 1;
        AddRowSlot.IsVisible = !atBottom;
    }

    private void DeleteSelected_Click(object? sender, RoutedEventArgs e)
    {
        Vm?.DeleteSelectedComponent();
    }

    // 组件右上角的删除按钮
    private void ComponentDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && sender is Button { DataContext: HomeComponentViewModel vm })
        {
            if (ReferenceEquals(Vm.SelectedComponent, vm))
            {
                Vm.DeleteSelectedComponent();
            }
            else
            {
                Vm.Home.RemoveComponent(vm);
            }
        }
    }

    private void RowDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && sender is Button { DataContext: HomeRowViewModel row })
        {
            Vm.Home.RemoveRow(row);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_pendingDrag != null && !_isDragging)
        {
            var delta = e.GetPosition(PageRoot) - _pressPos;
            if (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold)
            {
                BeginDrag(e);
            }
        }
        if (_isDragging)
        {
            UpdateDrag(e);
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            EndDrag(e);
        }
        else if (_pendingDrag is LibraryComponentItem item)
        {
            // 没拖动就是纯点击：组件库直接添加（指针已捕获到页面，Click 不会触发）
            Vm?.AddComponentFromLibrary(item);
        }
        else if (_pendingDrag is HomeComponentViewModel vm)
        {
            // 纯点击组件才选中（拖拽已在 EndDrag 里选中被拖组件）
            Vm?.SelectComponent(vm);
        }
        _pendingDrag = null;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging)
        {
            CleanupDrag();
        }
        _pendingDrag = null;
    }

    private void BeginDrag(PointerEventArgs e)
    {
        _isDragging = true;
        Cursor = DragCursor;

        DragGhostText.Text = _pendingDrag switch
        {
            HomeComponentViewModel c => HomeComponentRegistry.TryGet(c.Id)?.Title ?? c.Id,
            LibraryComponentItem i => i.Title,
            _ => string.Empty
        };
        DragGhost.IsVisible = true;
        _autoScrollTimer.Start();
        UpdateDrag(e);
    }

    private void UpdateDrag(PointerEventArgs e)
    {
        var pos = e.GetPosition(PageRoot);
        DragGhost.Margin = new Thickness(pos.X + 14, pos.Y + 12, 0, 0);

        // 指针进入预览可视区上下边缘带时，标记自动滚动方向。
        // 预览内部内容不足（无法滚动）时由 DoAutoScroll 兜底滚外层页面
        var scPos = e.GetPosition(PreviewScroll);
        var viewH = PreviewScroll.Viewport.Height;
        _autoScrollDir = 0;
        if (scPos.Y < AutoScrollEdge)
        {
            _autoScrollDir = -1;
        }
        else if (scPos.Y > viewH - AutoScrollEdge)
        {
            _autoScrollDir = 1;
        }

        var row = HitRowRoot(pos)?.DataContext as HomeRowViewModel;
        if (!ReferenceEquals(row, _dropTargetRow))
        {
            if (_dropTargetRow != null)
            {
                _dropTargetRow.IsDropTarget = false;
            }
            _dropTargetRow = row;
            if (row != null)
            {
                row.IsDropTarget = true;
            }
        }
    }

    // 拖拽自动滚动：优先滚预览内部，预览到边界（或内容不足滚不动）时滚外层设置页，
    // 保证拖到边缘总有可滚动的对象
    private void DoAutoScroll()
    {
        var sc = PreviewScroll;
        var maxY = Math.Max(0, sc.Extent.Height - sc.Viewport.Height);

        if (_autoScrollDir < 0 && sc.Offset.Y > 0.5)
        {
            sc.Offset = sc.Offset.WithY(Math.Max(0, sc.Offset.Y + _autoScrollDir * AutoScrollStep));
        }
        else if (_autoScrollDir > 0 && sc.Offset.Y < maxY - 0.5)
        {
            sc.Offset = sc.Offset.WithY(Math.Min(maxY, sc.Offset.Y + _autoScrollDir * AutoScrollStep));
        }
        else if (_autoScrollDir != 0 && PageScroll is { } page)
        {
            // 预览滚不动，滚整个设置页
            var pageMax = Math.Max(0, page.Extent.Height - page.Viewport.Height);
            page.Offset = page.Offset.WithY(Math.Clamp(page.Offset.Y + _autoScrollDir * AutoScrollStep * 2, 0, pageMax));
        }
    }

    private void OnAutoScrollTick(object? sender, EventArgs e)
    {
        if (!_isDragging || _autoScrollDir == 0) return;
        DoAutoScroll();
    }

    private void EndDrag(PointerReleasedEventArgs e)
    {
        var payload = _pendingDrag;
        var pos = e.GetPosition(PageRoot);
        CleanupDrag();

        if (Vm == null || payload == null)
        {
            return;
        }

        var rowRoot = HitRowRoot(pos);
        if (rowRoot?.DataContext is not HomeRowViewModel row)
        {
            return; // 没落在任何行上，当次拖拽取消
        }

        var index = CalcDropIndex(rowRoot, pos);

        if (payload is HomeComponentViewModel component)
        {
            Vm.Home.MoveComponent(component, row, index);
            Vm.SelectComponent(component);
        }
        else if (payload is LibraryComponentItem item)
        {
            var added = Vm.Home.AddComponentToRow(item.Descriptor.Id, row, index);
            if (added != null)
            {
                Vm.SelectComponent(added);
            }
        }
    }

    private void CleanupDrag()
    {
        _isDragging = false;
        _pendingDrag = null;
        _autoScrollTimer.Stop();
        _autoScrollDir = 0;
        if (_dropTargetRow != null)
        {
            _dropTargetRow.IsDropTarget = false;
            _dropTargetRow = null;
        }
        DragGhost.IsVisible = false;
        Cursor = null;
    }

    /// <summary>落点命中的行：遍历预览里所有行根节点，做坐标换算后的矩形包含判断</summary>
    private Panel? HitRowRoot(Point posInPage)
    {
        foreach (var panel in PreviewRoot.GetVisualDescendants().OfType<Panel>())
        {
            if (!panel.Classes.Contains(RowRootClass) || !panel.IsEffectivelyVisible)
            {
                continue;
            }
            // 两个角都做坐标换算，把 Viewbox 等的缩放一并考虑进去
            var topLeft = panel.TranslatePoint(new Point(0, 0), PageRoot);
            var bottomRight = panel.TranslatePoint(new Point(panel.Bounds.Width, panel.Bounds.Height), PageRoot);
            if (topLeft == null || bottomRight == null)
            {
                continue;
            }
            var bounds = new Rect(topLeft.Value, bottomRight.Value);
            if (bounds.Contains(posInPage))
            {
                return panel;
            }
        }
        return null;
    }

    /// <summary>按落点计算插入位置：找到第一个中点在落点之后的组件，插它前面，否则追加</summary>
    private static int CalcDropIndex(Panel rowRoot, Point posInPage)
    {
        // 组件容器是行内 ItemsControl（面板为水平 StackPanel），坐标换算已考虑 Viewbox 缩放
        var panel = rowRoot.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Where(ic => ic.IsEffectivelyVisible)
            .Select(ic => ic.ItemsPanelRoot)
            .OfType<StackPanel>()
            .FirstOrDefault();
        if (panel == null || panel.Children.Count == 0)
        {
            return 0;
        }

        var pos = rowRoot.TranslatePoint(posInPage, panel);
        if (pos == null)
        {
            return 0;
        }

        for (var i = 0; i < panel.Children.Count; i++)
        {
            var b = panel.Children[i].Bounds;
            if (pos.Value.X < b.X + b.Width / 2)
            {
                return i;
            }
        }
        return panel.Children.Count;
    }
}
