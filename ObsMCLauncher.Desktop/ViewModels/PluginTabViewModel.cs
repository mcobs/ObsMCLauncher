using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 插件标签页的ViewModel
/// 支持自定义UI内容（方案A）：插件可传入 Avalonia UserControl 作为标签页内容
/// </summary>
public partial class PluginTabViewModel : ViewModelBase
{
    private readonly object? _payload;

    public string PluginId { get; }
    public string TabId { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _contentText;

    /// <summary>
    /// 插件提供的自定义UI内容，非null时替代默认文本显示
    /// </summary>
    [ObservableProperty]
    private Control? _customContent;

    /// <summary>
    /// 是否有自定义UI内容
    /// </summary>
    public bool HasCustomContent => CustomContent != null;

    public PluginTabViewModel(string pluginId, string tabId, string title, object? payload, Control? customContent = null)
    {
        PluginId = pluginId;
        TabId = tabId;
        Title = title;
        _payload = payload;
        CustomContent = customContent;
        _contentText = $"这是插件 '{pluginId}' 的标签页 '{title}'。\n\n标签页ID: {tabId}";

        if (payload != null)
        {
            _contentText += $"\n\n附加数据: {payload}";
        }
    }

    partial void OnCustomContentChanged(Control? value)
    {
        OnPropertyChanged(nameof(HasCustomContent));
    }

    public void Initialize()
    {
        DebugLogger.Info("PluginTabViewModel", $"初始化插件标签页: {Title} (插件: {PluginId}, tabId: {TabId}, 自定义UI: {HasCustomContent})");
    }
}