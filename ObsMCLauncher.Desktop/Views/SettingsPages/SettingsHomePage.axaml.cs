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

    private static readonly Cursor DragCursor = new(StandardCursorType.DragMove);

    private Point _pressPos;
    private object? _pendingDrag;

    // 拖拽期间有效：当前拖的是组件还是组件库条目（进程内直接传引用）
    private bool _isDragging;
    private IPointer? _dragPointer;
    private HomeRowViewModel? _dropTargetRow;

    private SettingsHomeViewModel? Vm => DataContext as SettingsHomeViewModel;

    public SettingsHomePage()
    {
        InitializeComponent();
        // 捕获被系统拿走（窗口切换等）时收尾，避免卡在拖拽状态
        PointerCaptureLost += (_, _) =>
        {
            if (_isDragging)
            {
                CleanupDrag();
            }
            _pendingDrag = null;
        };
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

    // 组件外壳按下：先选中，位移超阈值后转为拖拽
    private void ComponentChrome_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm != null && sender is Border { DataContext: HomeComponentViewModel vm })
        {
            Vm.SelectComponent(vm);
            _pendingDrag = vm;
            _pressPos = e.GetPosition(PageRoot);
        }
    }

    // 组件库：点击直接添加
    private void LibraryChip_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && sender is Button { DataContext: LibraryComponentItem item })
        {
            Vm.AddComponentFromLibrary(item);
        }
    }

    // 组件库：按下后可拖拽到预览的行里
    private void LibraryChip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button { DataContext: LibraryComponentItem item })
        {
            _pendingDrag = item;
            _pressPos = e.GetPosition(PageRoot);
        }
    }

    private void AddRow_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        Vm.Home.InsertRow(Vm.Home.HomeRows.Count);
        // 等布局更新后滚到底部，让新行进入视野
        Dispatcher.UIThread.Post(PreviewScroll.ScrollToEnd, DispatcherPriority.Loaded);
    }

    private void DeleteSelected_Click(object? sender, RoutedEventArgs e)
    {
        Vm?.DeleteSelectedComponent();
    }

    private void RowPin_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && sender is Button { DataContext: HomeRowViewModel row })
        {
            Vm.Home.SetRowPinned(row, !row.IsPinnedToBottom);
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
                BeginDrag(e, _pendingDrag);
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
        _pendingDrag = null;
        base.OnPointerReleased(e);
    }

    private void BeginDrag(PointerEventArgs e, object payload)
    {
        _isDragging = true;
        _dragPointer = e.Pointer;
        _dragPointer.Capture(this);
        Cursor = DragCursor;

        DragGhostText.Text = payload switch
        {
            HomeComponentViewModel c => HomeComponentRegistry.TryGet(c.Id)?.Title ?? c.Id,
            LibraryComponentItem i => i.Title,
            _ => string.Empty
        };
        DragGhost.IsVisible = true;
        UpdateDrag(e);
    }

    private void UpdateDrag(PointerEventArgs e)
    {
        var pos = e.GetPosition(PageRoot);
        DragGhost.Margin = new Thickness(pos.X + 14, pos.Y + 12, 0, 0);

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
        _dragPointer?.Capture(null);
        _dragPointer = null;
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
            var topLeft = panel.TranslatePoint(new Point(0, 0), PageRoot);
            if (topLeft == null)
            {
                continue;
            }
            var bounds = new Rect(topLeft.Value, panel.Bounds.Size);
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
