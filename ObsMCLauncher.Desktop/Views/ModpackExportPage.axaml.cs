using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

/// <summary>
/// 实例页「导出」标签页（DataContext 为 <see cref="ModpackExportPageViewModel"/>）。
///
/// 这里承担一件 XAML 表达不了的事：把节点 VM 的展开状态搬到 <see cref="TreeViewItem"/> 上。
/// 用绑定驱动的两种写法（<c>ItemContainerTheme</c> 的 ControlTheme Setter、普通 Style 的 Setter）
/// 在 Avalonia 11.3.11 上都会在构建条目时抛 CLR internal error，只能改成命令式设置。
/// </summary>
public partial class ModpackExportPage : UserControl
{
    /// <summary>
    /// 迭代上限。展开是逐层的：父级展开后子容器才会被实现出来，
    /// 所以要"设一轮 → 跑一次布局 → 再设下一轮"。树最多 3 层（更深的目录按整目录导出），取 12 足够。
    /// </summary>
    private const int MaxExpansionPasses = 12;

    private ModpackExportPageViewModel? _viewModel;

    public ModpackExportPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.TreeExpansionRequested -= OnTreeExpansionRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as ModpackExportPageViewModel;

        if (_viewModel != null)
        {
            _viewModel.TreeExpansionRequested += OnTreeExpansionRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModpackExportPageViewModel.RootNodes))
            return;

        // 重新扫描后整棵树是新建的，容器要到这一轮布局跑完才存在
        Dispatcher.UIThread.Post(ApplyExpansionFromViewModel, DispatcherPriority.Loaded);
    }

    private void OnTreeExpansionRequested(bool _) => ApplyExpansionFromViewModel();

    /// <summary>把每个节点的 <c>IsExpanded</c> 逐层同步到 TreeViewItem 上。</summary>
    private void ApplyExpansionFromViewModel()
    {
        var tree = this.GetVisualDescendants().OfType<TreeView>().FirstOrDefault();
        if (tree == null)
            return;

        for (var pass = 0; pass < MaxExpansionPasses; pass++)
        {
            var changed = false;

            foreach (var item in tree.GetVisualDescendants().OfType<TreeViewItem>().ToList())
            {
                if (item.DataContext is not ModpackExportFileNode node)
                    continue;

                if (item.IsExpanded == node.IsExpanded)
                    continue;

                item.IsExpanded = node.IsExpanded;
                changed = true;
            }

            if (!changed)
                return;

            // 让这一轮布局把下一层容器实现出来，下一轮才能继续往下设
            tree.UpdateLayout();
        }
    }
}
