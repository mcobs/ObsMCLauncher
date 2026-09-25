using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 导出内容树节点。勾选状态是 <c>bool?</c> 三态：
/// <c>true</c> 全选 / <c>false</c> 全不选 / <c>null</c> 部分选中。
///
/// 级联规则：用户勾选（或取消）会<b>向下</b>传播到全部后代，随后<b>向上</b>重算父节点三态。
/// 达到深度上限的目录没有子节点，它自己就是整体开关。
///
/// 注意 <see cref="IsChecked"/> 是手写属性而非 <c>[ObservableProperty]</c>：
/// 内部需要"静默设置"（级联/汇总时不触发用户回调），MVVM 工具包会为字段访问报 MVVMTK0034。
/// </summary>
public partial class ModpackExportFileNode : ObservableObject
{
    /// <summary>默认展开到第几层（0 起）。前两层铺开，用户一进来就能看到主要内容。</summary>
    private const int DefaultExpandDepth = 2;

    /// <summary>用户点击勾选框后的回调（由页面 VM 注入，用来刷新底部统计）。</summary>
    private Action? _onUserToggled;

    private bool? _isChecked;

    public string Name { get; init; } = "";

    /// <summary>相对运行目录的路径（<c>/</c> 分隔）。</summary>
    public string RelativePath { get; init; } = "";

    public bool IsDirectory { get; init; }

    /// <summary>达到深度上限，不展开子节点（勾选即包含全部后代）。</summary>
    public bool IsDepthLimited { get; init; }

    public int Depth { get; init; }

    /// <summary>目录用途的中文标注（如"模组""光影包"），认不出来为空串。</summary>
    public string Purpose { get; init; } = "";

    /// <summary>整合包必需内容：复选框强制勾选且禁用，任何"取消勾选"的路径都会被纠正回来。</summary>
    public bool IsRequired { get; init; }

    public long Length { get; init; }

    /// <summary>含后代在内的文件数（目录）。</summary>
    public int TotalFileCount { get; init; }

    public long TotalBytes { get; init; }

    public ObservableCollection<ModpackExportFileNode> Children { get; } = new();

    /// <summary>构建时算出的默认勾选态，供"恢复默认"使用。</summary>
    public bool DefaultChecked { get; set; }

    /// <summary>
    /// 本节点<b>直接</b>承载的文件相对路径（普通目录只有自己那一层的直接文件；
    /// 达到深度上限的目录则是全部后代）——由页面 VM 在建树后回填，保证每个文件只属于一个节点。
    /// </summary>
    public List<string> OwnedFilePaths { get; } = new();

    internal ModpackExportFileNode? Parent { get; set; }

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasPurpose => IsDirectory && Purpose.Length > 0;

    /// <summary>右侧说明：目录是"N 项 · X MB"，文件是"X MB"（列宽固定，右对齐）。</summary>
    public string DetailText => IsDirectory
        ? $"{TotalFileCount} 项 · {FormatSize(TotalBytes)}"
        : FormatSize(Length);

    /// <summary>悬停提示：完整相对路径 + 用途；深度受限的目录额外说明它按整目录导出。</summary>
    public string TooltipText
    {
        get
        {
            var lines = new List<string> { RelativePath };
            if (HasPurpose)
                lines.Add($"用途：{Purpose}");
            if (IsRequired)
                lines.Add("整合包必需内容，一直包含在包里（不可取消）");
            if (IsDepthLimited)
                lines.Add("内容较多，不逐个展开；勾选即导出整个目录");
            return string.Join('\n', lines);
        }
    }

    /// <summary>三态勾选。UI 双向绑定走这里；程序内批量设置走 <see cref="SetCheckedRecursive"/> 等。</summary>
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            // 必选项不允许被取消：控件侧已经 IsEnabled=false，这里是防其它代码路径改它
            if (IsRequired)
                value = true;

            if (_isChecked == value)
                return;

            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));

            // 三态控件的点击序列是 true → null → false → true，中间那个 null 只是控件自身的循环行为：
            // 统一归一化成"勾选 / 取消"，并级联到全部后代。
            var target = value == true;
            foreach (var child in Children)
                child.SetCheckedValue(target);

            if (_isChecked != target)
            {
                _isChecked = target;
                OnPropertyChanged(nameof(IsChecked));
            }

            Parent?.RecomputeFromChildren();
            _onUserToggled?.Invoke();
        }
    }

    /// <summary>把整棵子树恢复到构建时的默认勾选态（自底向上汇总，保证父目录三态正确）。</summary>
    public void ApplyDefaults()
    {
        foreach (var child in Children)
            child.ApplyDefaults();

        if (Children.Count == 0)
        {
            SetCheckedValue(DefaultChecked);
            return;
        }

        var allChecked = Children.All(c => c.IsChecked == true);
        var noneChecked = Children.All(c => c.IsChecked == false);
        SetCheckedValue(allChecked ? true : noneChecked ? false : null);
    }

    /// <summary>全选 / 全不选（含全部后代）。</summary>
    public void SetCheckedRecursive(bool value) => SetCheckedValue(value);

    /// <summary>展开 / 收起整棵子树。</summary>
    public void SetExpandedRecursive(bool value)
    {
        if (Children.Count == 0)
            return;

        IsExpanded = value;
        foreach (var child in Children)
            child.SetExpandedRecursive(value);
    }

    /// <summary>
    /// 把未勾选的文件排除，收集真正要导出的文件路径。
    ///
    /// 目录被整选时<b>必须继续递归</b>：文件路径是挂在各自文件节点上的，
    /// 目录自己的 <see cref="OwnedFilePaths"/> 在正常目录上恒为空（只有深度受限目录才有内容）。
    /// </summary>
    public void CollectCheckedFiles(List<string> list)
    {
        if (_isChecked == false)
            return;

        if (!IsDirectory)
        {
            if (_isChecked == true)
                list.Add(RelativePath);
            return;
        }

        // 深度受限目录：后代没有节点，文件就挂在它身上
        list.AddRange(OwnedFilePaths);

        foreach (var child in Children)
            child.CollectCheckedFiles(list);
    }

    /// <summary>由 Core 的扫描节点构建 VM 节点（勾选态先按建议值给，目录随后自底向上重算）。</summary>
    public static ModpackExportFileNode Build(ModpackExportTreeNode source, Action? onUserToggled = null)
    {
        var node = new ModpackExportFileNode
        {
            Name = source.Name,
            RelativePath = source.RelativePath,
            IsDirectory = source.IsDirectory,
            IsDepthLimited = source.IsDepthLimited,
            IsRequired = source.IsRequired,
            Depth = source.Depth,
            Purpose = source.Purpose,
            Length = source.Length,
            TotalFileCount = source.FileCount,
            TotalBytes = source.TotalBytes,
            _isChecked = source.IsRequired || source.Suggestion == ModpackFileSuggestion.Suggested,
            _onUserToggled = onUserToggled
        };

        // 前两层默认展开（构建期设置，此时还没有任何订阅者）
        if (source.IsDirectory)
            node.IsExpanded = source.Depth < DefaultExpandDepth;

        foreach (var child in source.Children)
        {
            var childNode = Build(child, onUserToggled);
            childNode.Parent = node;
            node.Children.Add(childNode);
        }

        if (node.IsDirectory)
            node.RecomputeDefaultsFromChildren();

        return node;
    }

    /// <summary>
    /// 静默设置：值变化时通知 UI 并向下级联，<b>不</b>触发用户回调、不触发向上汇总。
    ///
    /// ⚠️ 通知必须显式写 <c>nameof(IsChecked)</c>：<c>OnPropertyChanged()</c> 的
    /// <c>[CallerMemberName]</c> 在<b>方法</b>里会取方法名（"SetCheckedValue"），
    /// 于是绑定收不到通知 —— 表现为「目录勾上了、子项界面纹丝不动」。
    /// </summary>
    private void SetCheckedValue(bool? value)
    {
        if (IsRequired)
            value = true;

        if (_isChecked == value)
            return;

        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));

        if (!value.HasValue)
            return;

        foreach (var child in Children)
            child.SetCheckedValue(value);
    }

    /// <summary>由子节点汇总自己的三态，并继续向上传递。</summary>
    private void RecomputeFromChildren()
    {
        if (Children.Count == 0)
            return;

        var anyChecked = Children.Any(c => c.IsChecked != false);
        var allChecked = Children.All(c => c.IsChecked == true);

        bool? next = allChecked ? true : anyChecked ? null : false;
        if (_isChecked != next)
        {
            _isChecked = next;
            OnPropertyChanged(nameof(IsChecked));
        }

        Parent?.RecomputeFromChildren();
    }

    /// <summary>
    /// 自底向上把目录的默认态对齐到子节点（全选 true / 全不选 false / 混合 null）。
    /// 没有子节点的目录（深度受限）保留自己的建议值当默认态。
    /// </summary>
    private void RecomputeDefaultsFromChildren()
    {
        if (Children.Count == 0)
        {
            DefaultChecked = IsRequired || _isChecked == true;
            return;
        }

        foreach (var child in Children)
            child.RecomputeDefaultsFromChildren();

        var allChecked = Children.All(c => c.IsChecked == true);
        var noneChecked = Children.All(c => c.IsChecked == false);

        DefaultChecked = IsRequired || allChecked;
        _isChecked = IsRequired || allChecked ? true : noneChecked ? false : null;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
