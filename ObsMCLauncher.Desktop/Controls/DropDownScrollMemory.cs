using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ObsMCLauncher.Desktop.Controls;

/// <summary>
/// 下拉框滚动位置记忆（附加属性）：再次展开下拉时，把列表滚回上次离开的位置。
///
/// 解决的问题：ComboBox 每次展开都会把当前选中项 ScrollIntoView。用户滚动浏览到一半、
/// 没换选中项就收回下拉，下次点开又会被拽回选中项那一行，长列表里很难受。
/// 挂上本行为后，展开位置以「用户上次滚到的地方」为准。
///
/// 状态只活在内存里（<see cref="ConditionalWeakTable{TKey,TValue}"/> 挂在 ComboBox 实例上），
/// 不写配置文件、进程退出即失效——这是刻意的：下拉位置属于一次会话内的手感，不是需要持久化的设置。
///
/// 用法：<c>&lt;ComboBox controls:DropDownScrollMemory.KeepPosition="True"&gt;</c>
/// </summary>
public static class DropDownScrollMemory
{
    /// <summary>展开下拉时是否记住/还原滚动位置</summary>
    public static readonly AttachedProperty<bool> KeepPositionProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>("KeepPosition", typeof(DropDownScrollMemory));

    private static readonly ConditionalWeakTable<ComboBox, Memory> _memories = new();

    static DropDownScrollMemory()
    {
        KeepPositionProperty.Changed.AddClassHandler<ComboBox>(OnKeepPositionChanged);
    }

    public static bool GetKeepPosition(ComboBox comboBox) => comboBox.GetValue(KeepPositionProperty);

    public static void SetKeepPosition(ComboBox comboBox, bool value) => comboBox.SetValue(KeepPositionProperty, value);

    /// <summary>单个下拉框的位置记忆（内存态）</summary>
    private sealed class Memory
    {
        /// <summary>用户最近停留的纵向偏移</summary>
        public double OffsetY;

        /// <summary>展开期间正在跟踪的滚动容器；关闭时用来退订</summary>
        public ScrollViewer? Watched;

        /// <summary>跟踪用的处理器，退订时必须拿到同一个委托实例</summary>
        public EventHandler<ScrollChangedEventArgs>? ScrollHandler;

        /// <summary>DropDownOpened/Closed 是否已挂上（附加属性可能被反复赋值）</summary>
        public bool EventsHooked;
    }

    private static void OnKeepPositionChanged(ComboBox comboBox, AvaloniaPropertyChangedEventArgs args)
    {
        var memory = _memories.GetOrCreateValue(comboBox);

        if (args.NewValue is true)
        {
            if (memory.EventsHooked) return;
            comboBox.DropDownOpened += OnDropDownOpened;
            comboBox.DropDownClosed += OnDropDownClosed;
            memory.EventsHooked = true;
        }
        else
        {
            if (!memory.EventsHooked) return;
            comboBox.DropDownOpened -= OnDropDownOpened;
            comboBox.DropDownClosed -= OnDropDownClosed;
            memory.EventsHooked = false;
            DetachTracking(memory);
        }
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        var memory = _memories.GetOrCreateValue(comboBox);

        // 两个时序问题都靠这个 Post 解决：
        // ① ComboBox 展开时自己会把选中项滚进视野，会覆盖我们要还原的位置；
        // ② Popup 内容刚 attach，Extent/Offset 还是上一轮的旧值，这时读写都没意义。
        // BringIntoView 与首次布局都在 Layout 优先级处理，而 Layout(8) > Loaded(6)，
        // 所以排到 Loaded 就一定能排在「展开引发的布局」之后。
        Dispatcher.UIThread.Post(() => RestoreAndTrack(comboBox, memory), DispatcherPriority.Loaded);
    }

    private static void OnDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        if (!_memories.TryGetValue(comboBox, out var memory)) return;

        // 这里读不到 ScrollViewer.Offset：Popup 关闭后内容已从 OverlayPopupHost 脱离视觉树。
        // 位置在展开期间已经持续记录好了，这里只负责退订。
        DetachTracking(memory);
    }

    private static void RestoreAndTrack(ComboBox comboBox, Memory memory)
    {
        var scrollViewer = FindScrollViewer(comboBox);
        if (scrollViewer is null) return;

        // 下拉可能在还原之前就被收回了（快速点开点关）
        if (!comboBox.IsDropDownOpen) return;

        var saved = memory.OffsetY;
        if (saved > 0 && Math.Abs(scrollViewer.Offset.Y - saved) > 0.5)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, saved);
        }

        DetachTracking(memory);
        memory.ScrollHandler = (_, _) => memory.OffsetY = scrollViewer.Offset.Y;
        scrollViewer.ScrollChanged += memory.ScrollHandler;
        memory.Watched = scrollViewer;
    }

    private static void DetachTracking(Memory memory)
    {
        if (memory.Watched is { } watched && memory.ScrollHandler is { } handler)
        {
            watched.ScrollChanged -= handler;
        }

        memory.Watched = null;
        memory.ScrollHandler = null;
    }

    /// <summary>
    /// 找出下拉列表的滚动容器。
    /// 注意不能只扫 <c>comboBox.GetVisualDescendants()</c>：Popup 打开时它的内容被挂到
    /// OverlayPopupHost / PopupRoot 上，已经不属于 ComboBox 的视觉树了，
    /// 必须从模板里的 <see cref="Popup"/> 元素（它仍在视觉树内）经由 Child 进去找。
    /// </summary>
    private static ScrollViewer? FindScrollViewer(ComboBox comboBox)
    {
        var popup = comboBox.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
        if (popup?.Child is { } popupContent)
        {
            var scrollViewers = popupContent.GetSelfAndVisualDescendants().OfType<ScrollViewer>();
            var named = scrollViewers.FirstOrDefault(sv => sv.Name == "PART_ScrollViewer");
            if (named is not null) return named;

            var any = scrollViewers.FirstOrDefault();
            if (any is not null) return any;
        }

        // 兜底：模板不把列表放进 Popup 的情况
        return comboBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }
}
