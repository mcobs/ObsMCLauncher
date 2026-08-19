using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>组件库条目（同一组件可重复添加，无"已添加"状态）</summary>
public sealed class LibraryComponentItem
{
    public required HomeComponentDescriptor Descriptor { get; init; }

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

        // 延迟到 UI 空闲后刷新，确保 HomeViewModel 的 BuildHomeRows 已完成
        Dispatcher.UIThread.Post(() =>
        {
            EnsureDataReady();
            RefreshLibrary();
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>确保 HomeComponentRegistry 和 Home.HomeRows 已就绪</summary>
    private void EnsureDataReady()
    {
        // 强制触发 HomeComponentRegistry 静态构造函数
        // （访问任意静态成员即可触发，这里用 TryGet 来确保）
        HomeComponentRegistry.TryGet(HomeComponentRegistry.WelcomeId);
        var registryCount = HomeComponentRegistry.GetAll().Count;

        if (registryCount == 0)
        {
            DebugLogger.Warn("SettingsHome", "HomeComponentRegistry is empty after forcing init");
        }

        // 如果主页行为空，强制重建
        if (Home.HomeRows.Count == 0)
        {
            DebugLogger.Warn("SettingsHome", "Home.HomeRows is empty, forcing rebuild");
            Home.ForceRebuildRows();
        }
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
    }

    /// <summary>点击组件库条目添加组件：优先加到选中组件所在行，否则加到最后一行</summary>
    public void AddComponentFromLibrary(LibraryComponentItem item)
    {
        var row = SelectedComponent != null
            ? Home.HomeRows.FirstOrDefault(r => r.Components.Contains(SelectedComponent))
            : null;
        if (row == null)
        {
            row = Home.HomeRows.LastOrDefault();
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
    }

    /// <summary>从注册表重建组件库（同一组件可重复添加，条目无状态）</summary>
    public void RefreshLibrary()
    {
        LibraryGroups.Clear();
        foreach (var group in HomeComponentRegistry.GetGrouped())
        {
            var vm = new LibraryGroupViewModel { DisplayName = group.DisplayName };
            foreach (var descriptor in group.Components)
            {
                vm.Items.Add(new LibraryComponentItem { Descriptor = descriptor });
            }
            LibraryGroups.Add(vm);
        }
    }
}
