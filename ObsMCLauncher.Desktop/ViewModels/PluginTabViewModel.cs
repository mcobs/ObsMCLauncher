using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 插件标签页的ViewModel
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

    public PluginTabViewModel(string pluginId, string tabId, string title, object? payload)
    {
        PluginId = pluginId;
        TabId = tabId;
        Title = title;
        _payload = payload;
        _contentText = $"这是插件 '{pluginId}' 的标签页 '{title}'。\n\n标签页ID: {tabId}";

        if (payload != null)
        {
            _contentText += $"\n\n附加数据: {payload}";
        }
    }

    public void Initialize()
    {
        DebugLogger.Info("PluginTabViewModel", $"初始化插件标签页: {_title} (插件: {PluginId}, tabId: {TabId})");
    }
}