using System;
using System.Collections.Generic;
using System.IO;

namespace ObsMCLauncher.Core.Plugins;

/// <summary>
/// 插件上下文实现
/// </summary>
public class PluginContext : IPluginContext
{
    private readonly string _pluginId;
    private readonly string _pluginDataDir;

    private static readonly Dictionary<string, List<Action<object?>>> _eventHandlers = new();

    public static Action<string, string, string, string?, object?>? OnTabRegistered { get; set; }

    public static Action<string, string>? OnTabUnregistered { get; set; }

    public static Action<string, string, string, string?, string?, object?>? OnHomeCardRegistered { get; set; }

    public static Action<string>? OnHomeCardUnregistered { get; set; }

    public static Func<string, string, string, int?, string>? OnShowNotification { get; set; }

    public static Action<string, string, double?>? OnUpdateNotification { get; set; }

    public static Action<string>? OnCloseNotification { get; set; }

    public PluginContext(string pluginId)
    {
        _pluginId = pluginId;

        _pluginDataDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "OMCL",
            "plugins",
            pluginId
        );
    }

    public string LauncherVersion => "1.0.0";

    public string PluginDataDirectory => _pluginDataDir;

    public void RegisterTab(string title, string tabId, string? icon = null, object? payload = null)
    {
        OnTabRegistered?.Invoke(_pluginId, title, tabId, icon, payload);
    }

    public void UnregisterTab(string tabId)
    {
        OnTabUnregistered?.Invoke(_pluginId, tabId);
    }

    public void SubscribeEvent(string eventName, Action<object?> handler)
    {
        if (!_eventHandlers.ContainsKey(eventName))
        {
            _eventHandlers[eventName] = new List<Action<object?>>();
        }

        _eventHandlers[eventName].Add(handler);
    }

    public void PublishEvent(string eventName, object? eventData)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler(eventData);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginContext] 事件处理器异常: {ex.Message}");
                }
            }
        }
    }

    public void RegisterHomeCard(
        string cardId,
        string title,
        string description,
        string? icon = null,
        string? commandId = null,
        object? payload = null)
    {
        var fullCardId = $"{_pluginId}.{cardId}";
        OnHomeCardRegistered?.Invoke(fullCardId, title, description, icon, commandId, payload);
    }

    public void UnregisterHomeCard(string cardId)
    {
        var fullCardId = $"{_pluginId}.{cardId}";
        OnHomeCardUnregistered?.Invoke(fullCardId);
    }

    public string ShowNotification(string title, string message, string type = "info", int? durationSeconds = null)
    {
        return OnShowNotification?.Invoke(title, message, type, durationSeconds) ?? string.Empty;
    }

    public void UpdateNotification(string notificationId, string message, double? progress = null)
    {
        OnUpdateNotification?.Invoke(notificationId, message, progress);
    }

    public void CloseNotification(string notificationId)
    {
        OnCloseNotification?.Invoke(notificationId);
    }

    public static void TriggerGlobalEvent(string eventName, object? eventData)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler(eventData);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginContext] 全局事件处理器异常: {ex.Message}");
                }
            }
        }
    }
}
