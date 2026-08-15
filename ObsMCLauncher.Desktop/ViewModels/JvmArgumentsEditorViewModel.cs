#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>JVM 参数预设方案。</summary>
public sealed record JvmPreset(string Name, string Arguments, string Description)
{
    public override string ToString() => Name;
}

/// <summary>单个 JVM 快捷参数开关（chip）。</summary>
public partial class JvmFlagChip : ObservableObject
{
    /// <summary>完整参数 token，如 -XX:+UseG1GC。</summary>
    public string Flag { get; }

    /// <summary>界面上显示的短名称。</summary>
    public string Display { get; }

    /// <summary>悬停提示说明。</summary>
    public string Tooltip { get; }

    /// <summary>互斥分组（如 GC 参数同组互斥），null 表示独立开关。</summary>
    public string? Group { get; }

    /// <summary>点击开关时的回调（chip, 新状态）。</summary>
    public Action<JvmFlagChip, bool>? ToggleRequested { get; set; }

    [ObservableProperty]
    private bool _isEnabled;

    public JvmFlagChip(string flag, string display, string tooltip, string? group = null)
    {
        Flag = flag;
        Display = display;
        Tooltip = tooltip;
        Group = group;
    }

    partial void OnIsEnabledChanged(bool value) => ToggleRequested?.Invoke(this, value);
}

/// <summary>
/// JVM 参数编辑器（版本实例页与设置页共用）：
/// 常用参数 chips 开关 + 预设方案下拉 + 自由编辑多行文本框，三者双向同步。
/// 通过 <see cref="ArgumentsCommit"/> 回调把最终参数写回宿主（实例 init.json 或全局配置）。
/// </summary>
public partial class JvmArgumentsEditorViewModel : ViewModelBase
{
    /// <summary>参数变化后的提交回调（携带最终参数文本）。</summary>
    public Action<string>? ArgumentsCommit { get; set; }

    /// <summary>常用参数开关列表。</summary>
    public ObservableCollection<JvmFlagChip> Chips { get; } = new();

    /// <summary>预设方案列表。</summary>
    public ObservableCollection<JvmPreset> Presets { get; } = BuildPresets();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArguments))]
    private string _arguments = "";

    [ObservableProperty]
    private JvmPreset? _selectedPreset;

    public bool HasArguments => !string.IsNullOrWhiteSpace(Arguments);

    /// <summary>防止参数文本 / chips / 预设之间同步时产生回环。</summary>
    private bool _syncing;

    public JvmArgumentsEditorViewModel()
    {
        Chips.Add(new JvmFlagChip("-XX:+UseG1GC", "G1GC", "G1 垃圾回收器，适合大多数场景（推荐）", "GC") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-XX:+UseZGC", "ZGC", "低延迟垃圾回收器，追求流畅可用（需 Java 15+）", "GC") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-XX:+UseShenandoahGC", "Shenandoah", "低停顿垃圾回收器（部分发行版不支持）", "GC") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-XX:+UseParallelGC", "ParallelGC", "并行垃圾回收器，高吞吐", "GC") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-XX:+AlwaysPreTouch", "预分配内存", "启动时预分配内存，运行更稳定但启动稍慢") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-XX:+DisableExplicitGC", "禁用显式GC", "忽略代码中显式的 System.gc 调用，减少卡顿") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-XX:+UseStringDeduplication", "字符串去重", "对字符串去重节省内存（需配合 G1GC）") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-XX:+OptimizeStringConcat", "优化字符串拼接", "优化字符串拼接性能") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-Dfml.ignoreInvalidMinecraftCertificates=true", "跳过旧版证书校验", "老版本 Forge 跳过失效的 Minecraft 证书校验") { ToggleRequested = ToggleChip });
        Chips.Add(new JvmFlagChip("-Dfml.ignorePatchDiscrepancies=true", "忽略补丁差异", "老版本 Forge 忽略补丁差异检查") { ToggleRequested = ToggleChip });
    }

    private static ObservableCollection<JvmPreset> BuildPresets() => new()
    {
        new JvmPreset("空白（默认）", "", "清空全部参数，由启动器决定"),
        new JvmPreset("均衡 G1GC", "-XX:+UseG1GC -XX:+DisableExplicitGC", "适合大多数整合包"),
        new JvmPreset("低内存优化", "-XX:+UseG1GC -XX:+DisableExplicitGC -XX:+UseStringDeduplication", "内存紧张时使用"),
        new JvmPreset("高配流畅", "-XX:+UseZGC -XX:+AlwaysPreTouch -XX:+DisableExplicitGC", "高配机器，追求低延迟"),
        new JvmPreset("老版本兼容", "-XX:+UseG1GC -Dfml.ignoreInvalidMinecraftCertificates=true -Dfml.ignorePatchDiscrepancies=true", "老版本 Forge 模组"),
    };

    /// <summary>加载初始参数（不触发提交回调）。</summary>
    public void SetArguments(string? arguments)
    {
        _syncing = true;
        Arguments = arguments?.Trim() ?? "";
        _syncing = false;
        SyncChipsFromText();
        SyncPresetSelectionFromText();
    }

    partial void OnArgumentsChanged(string value)
    {
        if (_syncing) return;

        SyncChipsFromText();
        SyncPresetSelectionFromText();
        ArgumentsCommit?.Invoke(value);
    }

    partial void OnSelectedPresetChanged(JvmPreset? value)
    {
        if (_syncing || value == null) return;

        _syncing = true;
        Arguments = value.Arguments;
        _syncing = false;

        SyncChipsFromText();
        ArgumentsCommit?.Invoke(Arguments);
    }

    [RelayCommand]
    private void Clear()
    {
        if (string.IsNullOrEmpty(Arguments)) return;

        _syncing = true;
        Arguments = "";
        _syncing = false;

        SyncChipsFromText();
        SyncPresetSelectionFromText();
        ArgumentsCommit?.Invoke("");
    }

    /// <summary>点击 chip 开关：在参数文本中增删对应 token（同组开关互斥）。</summary>
    private void ToggleChip(JvmFlagChip chip, bool enabled)
    {
        if (_syncing) return;

        var tokens = Tokenize(Arguments);
        var index = FindToken(tokens, chip.Flag);

        if (enabled && index < 0)
        {
            _syncing = true;
            try
            {
                // 同组互斥：先移除组内其它已启用参数（如 G1GC / ZGC 只能选一个）
                if (chip.Group != null)
                {
                    foreach (var other in Chips)
                    {
                        if (other == chip || other.Group != chip.Group) continue;

                        var otherIndex = FindToken(tokens, other.Flag);
                        if (otherIndex >= 0)
                        {
                            tokens.RemoveAt(otherIndex);
                            other.IsEnabled = false; // 重入被 _syncing 拦截
                        }
                    }
                }
                tokens.Add(chip.Flag);
            }
            finally
            {
                _syncing = false;
            }
        }
        else if (!enabled && index >= 0)
        {
            tokens.RemoveAt(index);
        }
        else
        {
            return;
        }

        var text = string.Join(" ", tokens);
        if (text == Arguments) return;

        _syncing = true;
        Arguments = text;
        _syncing = false;

        SyncPresetSelectionFromText();
        ArgumentsCommit?.Invoke(text);
    }

    /// <summary>根据参数文本刷新 chips 开关状态。</summary>
    private void SyncChipsFromText()
    {
        var tokens = Tokenize(Arguments);

        foreach (var chip in Chips)
        {
            var shouldBeOn = FindToken(tokens, chip.Flag) >= 0;
            if (chip.IsEnabled == shouldBeOn) continue;

            _syncing = true;
            chip.IsEnabled = shouldBeOn;
            _syncing = false;
        }
    }

    /// <summary>参数文本与预设完全一致时选中对应预设，否则清除预设选中。</summary>
    private void SyncPresetSelectionFromText()
    {
        var trimmed = Arguments.Trim();
        var match = Presets.FirstOrDefault(p => string.Equals(p.Arguments, trimmed, StringComparison.Ordinal));

        if (ReferenceEquals(SelectedPreset, match)) return;

        _syncing = true;
        SelectedPreset = match;
        _syncing = false;
    }

    private static List<string> Tokenize(string text) =>
        (text ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static int FindToken(List<string> tokens, string flag) =>
        tokens.FindIndex(t => string.Equals(t, flag, StringComparison.OrdinalIgnoreCase));
}
