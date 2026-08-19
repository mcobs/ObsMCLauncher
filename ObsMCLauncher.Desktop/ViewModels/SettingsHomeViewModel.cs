using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>组件库条目</summary>
public sealed class LibraryComponentItem : ViewModelBase
{
    public required HomeComponentDescriptor Descriptor { get; init; }

    private bool _isAdded;
    public bool IsAdded
    {
        get => _isAdded;
        set => SetProperty(ref _isAdded, value);
    }

    public string Title => Descriptor.Title;

    public string Description => Descriptor.Description;
}

/// <summary>组件库分组</summary>
public sealed class LibraryGroupViewModel
{
    public string DisplayName { get; init; } = string.Empty;

    public ObservableCollection<LibraryComponentItem> Items { get; } = new();
}

/// <summary>设置页"主页自定义"的编辑器状态</summary>
public sealed class SettingsHomeViewModel : ViewModelBase
{
    public HomeViewModel Home { get; }

    public ObservableCollection<LibraryGroupViewModel> LibraryGroups { get; } = new();

    private HomeComponentViewModel? _selectedComponent;

    /// <summary>当前选中的组件（编辑器工具条操作对象）</summary>
    public HomeComponentViewModel? SelectedComponent
    {
        get => _selectedComponent;
        private set
        {
            if (_selectedComponent != null)
            {
                _selectedComponent.IsEditorSelected = false;
            }
            SetProperty(ref _selectedComponent, value);
            if (_selectedComponent != null)
            {
                _selectedComponent.IsEditorSelected = true;
            }
            OnPropertyChanged(nameof(SelectedComponentSizeIndex));
            OnPropertyChanged(nameof(HasSelectedComponent));
            OnPropertyChanged(nameof(SelectedComponentTitle));
        }
    }

    /// <summary>是否有选中的组件（操作条的显隐用）</summary>
    public bool HasSelectedComponent => SelectedComponent != null;

    /// <summary>选中组件的显示名</summary>
    public string SelectedComponentTitle =>
        SelectedComponent == null
            ? string.Empty
            : HomeComponentRegistry.TryGet(SelectedComponent.Id)?.Title ?? SelectedComponent.Id;

    public static readonly string[] SizeOptionNames = ["紧凑", "标准", "加宽", "整行"];

    /// <summary>选中组件的尺寸档位索引（四档），无选中时为 -1</summary>
    public int SelectedComponentSizeIndex
    {
        get => SelectedComponent == null ? -1 : (int)SelectedComponent.Size;
        set
        {
            if (SelectedComponent != null && value >= 0)
            {
                Home.SetComponentSize(SelectedComponent, (HomeCardSize)value);
                OnPropertyChanged();
            }
        }
    }

    public IRelayCommand ResetLayoutCommand { get; }

    public SettingsHomeViewModel(HomeViewModel home)
    {
        Home = home;
        ResetLayoutCommand = new RelayCommand(() =>
        {
            SelectedComponent = null;
            Home.ResetHomeLayout();
            RefreshLibrary();
        });

        RefreshLibrary();
    }

    /// <summary>选中组件（点击编辑器中的组件时调用）</summary>
    public void SelectComponent(HomeComponentViewModel? component)
    {
        SelectedComponent = component;
    }

    /// <summary>删除选中组件</summary>
    public void DeleteSelectedComponent()
    {
        if (SelectedComponent == null) return;
        Home.RemoveComponent(SelectedComponent);
        SelectedComponent = null;
        RefreshLibrary();
    }

    /// <summary>点击组件库条目添加组件：优先加到选中组件所在行，否则加到最后的滚动行</summary>
    public void AddComponentFromLibrary(LibraryComponentItem item)
    {
        if (item.IsAdded) return;

        var row = SelectedComponent != null
            ? Home.HomeRows.FirstOrDefault(r => r.Components.Contains(SelectedComponent))
            : null;
        if (row == null)
        {
            row = Home.ScrollableRows.LastOrDefault() ?? Home.HomeRows.LastOrDefault();
        }
        if (row == null)
        {
            row = Home.InsertRow(Home.HomeRows.Count);
        }

        var added = Home.AddComponentToRow(item.Descriptor.Id, row, row.Components.Count);
        if (added != null)
        {
            SelectedComponent = added;
        }
        RefreshLibrary();
    }

    /// <summary>从注册表重建组件库（含"已添加"状态）</summary>
    public void RefreshLibrary()
    {
        LibraryGroups.Clear();
        foreach (var group in HomeComponentRegistry.GetGrouped())
        {
            var vm = new LibraryGroupViewModel { DisplayName = group.DisplayName };
            foreach (var descriptor in group.Components)
            {
                vm.Items.Add(new LibraryComponentItem
                {
                    Descriptor = descriptor,
                    IsAdded = Home.HomeRows.Any(r => r.Components.Any(c => c.Id == descriptor.Id))
                });
            }
            LibraryGroups.Add(vm);
        }
    }
}
