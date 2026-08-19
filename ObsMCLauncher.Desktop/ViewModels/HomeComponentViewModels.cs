using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 主页组件运行时视图模型。渲染层按具体子类型匹配 DataTemplate，
/// 尺寸档位决定组件在行内的最小宽度。
/// 操作区（账号/版本/启动/日志开关）已去组件化，不再有对应组件类型。
/// </summary>
public abstract class HomeComponentViewModel : ViewModelBase
{
    public string Id { get; set; } = string.Empty;

    /// <summary>宿主 HomeViewModel，卡片类组件通过它绑定数据与命令</summary>
    public HomeViewModel Owner { get; set; } = null!;

    /// <summary>卡片类组件的数据源（标题/图标/命令等）</summary>
    public HomeCardInfo? Card { get; set; }

    private HomeCardSize _size;
    public HomeCardSize Size
    {
        get => _size;
        set
        {
            if (SetProperty(ref _size, value))
            {
                OnPropertyChanged(nameof(MinWidth));
            }
        }
    }

    /// <summary>
    /// 档位对应的最小宽度。Fill 组件独占一行时拉伸整行；
    /// 与其他组件同行时按固定宽度参与一排到底的排列。
    /// </summary>
    public double MinWidth => MinWidthOf(Size);

    /// <summary>档位对应的最小宽度（供行容量校验等场景静态计算）</summary>
    public static double MinWidthOf(HomeCardSize size) => size switch
    {
        HomeCardSize.Small => 120,
        HomeCardSize.Medium => 180,
        HomeCardSize.Large => 380,
        _ => 560
    };

    private bool _isEditorSelected;

    /// <summary>编辑器中是否被选中（仅运行时状态，不持久化）</summary>
    public bool IsEditorSelected
    {
        get => _isEditorSelected;
        set => SetProperty(ref _isEditorSelected, value);
    }
}

/// <summary>欢迎横幅</summary>
public sealed class WelcomeComponentViewModel : HomeComponentViewModel;

/// <summary>通用数据卡片（内置导航卡片与插件注册的卡片）</summary>
public sealed class CardComponentViewModel : HomeComponentViewModel;

/// <summary>主页的一行，行内组件一排到底，放不下时整行等比缩小</summary>
public sealed class HomeRowViewModel : ViewModelBase
{
    public ObservableCollection<HomeComponentViewModel> Components { get; } = new();

    /// <summary>行内恰好一个 Fill 组件时整行拉伸渲染（欢迎卡独占一行的场景）</summary>
    public bool IsSingleFill => Components.Count == 1 && Components[0].Size == HomeCardSize.Fill;

    /// <summary>空行（编辑器里显示占位提示用）</summary>
    public bool IsEmpty => Components.Count == 0;

    private bool _isDropTarget;

    /// <summary>编辑器拖拽时当前落点行高亮（仅运行时状态，不持久化）</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    public HomeRowViewModel()
    {
        Components.CollectionChanged += OnComponentsChanged;
    }

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsSingleFill));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// 行是否还能容纳一个指定档位的组件：Fill 只能独占一行，
    /// 常规组件按最小宽度之和与行宽上限比较（exclude 用于组件改尺寸/换行时排除自身）。
    /// </summary>
    public bool CanAccept(HomeCardSize size, HomeComponentViewModel? exclude = null)
    {
        var others = (exclude == null
            ? Components
            : Components.Where(c => !ReferenceEquals(c, exclude))).ToList();

        // Fill 独占一行：目标组件是 Fill 时行里不能有别的组件
        if (size == HomeCardSize.Fill)
        {
            return others.Count == 0;
        }

        // 行里已有 Fill 时不能再加常规组件
        if (others.Any(c => c.Size == HomeCardSize.Fill))
        {
            return false;
        }

        var used = others.Sum(c => c.MinWidth);
        return used + HomeComponentViewModel.MinWidthOf(size) <= HomeLayoutConfig.MaxRowWidth;
    }
}
