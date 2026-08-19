using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views.SettingsPages;

public partial class SettingsHomePage : UserControl
{
    // 超过这个距离才算拖拽，否则当作点击
    private const double DragThreshold = 4;

    private Point _pressPos;
    private object? _pendingDrag;

    // 拖拽期间有效：当前拖的是组件还是组件库条目（进程内直接传引用）
    private object? _activeDrag;

    private SettingsHomeViewModel? Vm => DataContext as SettingsHomeViewModel;

    public SettingsHomePage()
    {
        InitializeComponent();
        // 拖放事件挂在预览区根节点，统一从落点向上找目标行
        PreviewRoot.AddHandler(DragDrop.DragOverEvent, Preview_DragOver);
        PreviewRoot.AddHandler(DragDrop.DropEvent, Preview_Drop);
    }

    // 页面经 Frame 导航创建，DataContext 继承的是 SettingsViewModel，
    // 这里换成主页编辑器自己的视图模型
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel svm && svm.SettingsHome != null)
        {
            DataContext = svm.SettingsHome;
        }
    }

    // 组件外壳按下：先选中，位移超阈值后转为拖拽
    private void ComponentChrome_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm != null && sender is Border { DataContext: HomeComponentViewModel vm })
        {
            Vm.SelectComponent(vm);
            _pendingDrag = vm;
            _pressPos = e.GetPosition(null);
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
            _pressPos = e.GetPosition(null);
        }
    }

    private void AddRow_Click(object? sender, RoutedEventArgs e)
    {
        Vm?.Home.InsertRow(Vm.Home.HomeRows.Count);
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
            Vm.RefreshLibrary();
        }
    }

    private void Preview_DragOver(object? sender, DragEventArgs e)
    {
        if (_activeDrag != null)
        {
            e.DragEffects = DragDropEffects.Move | DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Preview_Drop(object? sender, DragEventArgs e)
    {
        var payload = _activeDrag;
        _activeDrag = null;
        if (Vm == null || payload == null)
        {
            return;
        }

        // 落点向上找行容器；没落在任何行上就忽略
        var rowItems = FindRowItems(e.Source as Visual);
        if (rowItems == null)
        {
            e.Handled = true;
            return;
        }
        var row = (HomeRowViewModel)rowItems.DataContext!;

        var index = CalcDropIndex(rowItems, e);

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
            Vm.RefreshLibrary();
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_pendingDrag != null)
        {
            var delta = e.GetPosition(null) - _pressPos;
            if (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold)
            {
                var payload = _pendingDrag;
                _pendingDrag = null;
                StartDrag(e, payload);
            }
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // 没拖起来就算了，点击交给 Click/选中处理
        _pendingDrag = null;
        base.OnPointerReleased(e);
    }

    private async void StartDrag(PointerEventArgs e, object payload)
    {
        _activeDrag = payload;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(
            payload is LibraryComponentItem ? "OMCL/LibraryComponent" : "OMCL/HomeComponent"));

        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            _activeDrag = null;
        }
    }

    /// <summary>从落点元素向上找渲染行的 ItemsControl（DataContext 是 HomeRowViewModel）</summary>
    private static ItemsControl? FindRowItems(Visual? source)
    {
        while (source != null)
        {
            if (source is ItemsControl { DataContext: HomeRowViewModel })
            {
                return (ItemsControl)source;
            }
            source = source.GetVisualParent();
        }
        return null;
    }

    /// <summary>按落点计算插入位置：找到第一个中点在落点之后的子元素，插它前面，否则追加</summary>
    private static int CalcDropIndex(ItemsControl rowItems, DragEventArgs e)
    {
        if (rowItems.ItemsPanelRoot is not { } panel)
        {
            return 0;
        }

        var pos = e.GetPosition(panel);
        var children = panel.Children;
        for (var i = 0; i < children.Count; i++)
        {
            var b = children[i].Bounds;
            // 换行的行用纵向判断，同行用横向判断
            if (pos.Y < b.Y + b.Height / 2 || (pos.Y < b.Bottom && pos.X < b.X + b.Width / 2))
            {
                return i;
            }
        }
        return children.Count;
    }
}
