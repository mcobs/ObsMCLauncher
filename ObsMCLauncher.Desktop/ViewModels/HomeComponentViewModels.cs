using System.Collections.Specialized;
using System.Collections.ObjectModel;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 主页组件运行时视图模型。渲染层按具体子类型匹配 DataTemplate，
/// 尺寸档位决定组件在行内的最小宽度。
/// </summary>
public abstract class HomeComponentViewModel : ViewModelBase
{
    public string Id { get; set; } = string.Empty;

    /// <summary>宿主 HomeViewModel，操作类组件通过它绑定账号/版本/启动等数据</summary>
    public HomeViewModel Owner { get; set; } = null!;

    /// <summary>卡片类组件的数据源（标题/图标/命令等），操作类组件为 null</summary>
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
    /// 与其他组件同行时按固定宽度参与换行。
    /// </summary>
    public double MinWidth => Size switch
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

/// <summary>账号选择区</summary>
public sealed class AccountPickerComponentViewModel : HomeComponentViewModel;

/// <summary>版本选择区</summary>
public sealed class VersionPickerComponentViewModel : HomeComponentViewModel;

/// <summary>启动按钮</summary>
public sealed class LaunchButtonComponentViewModel : HomeComponentViewModel;

/// <summary>游戏日志开关</summary>
public sealed class LogToggleComponentViewModel : HomeComponentViewModel;

/// <summary>水平分隔线</summary>
public sealed class SeparatorComponentViewModel : HomeComponentViewModel;

/// <summary>插件自定义内容组件，Content 为插件工厂创建的控件实例</summary>
public sealed class CustomContentComponentViewModel : HomeComponentViewModel
{
    public object? Content { get; init; }
}

/// <summary>主页的一行，行内组件横向排列自动换行</summary>
public sealed class HomeRowViewModel : ViewModelBase
{
    public ObservableCollection<HomeComponentViewModel> Components { get; } = new();

    private bool _isPinnedToBottom;

    /// <summary>固定到底部：不随卡片区滚动，始终显示在主页底部</summary>
    public bool IsPinnedToBottom
    {
        get => _isPinnedToBottom;
        set => SetProperty(ref _isPinnedToBottom, value);
    }

    /// <summary>行内恰好一个 Fill 组件时整行拉伸渲染（欢迎卡、分隔线独占一行的场景）</summary>
    public bool IsSingleFill => Components.Count == 1 && Components[0].Size == HomeCardSize.Fill;

    public HomeRowViewModel()
    {
        Components.CollectionChanged += OnComponentsChanged;
    }

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsSingleFill));
    }
}
