using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

public class PluginContext : IPluginContext
{
    private readonly string _pluginId;
    private readonly string _pluginDataDir;

    private static readonly Dictionary<string, List<Action<object?>>> _eventHandlers = new();

    private static readonly Dictionary<string, Action<object?>> _pluginCommands = new();
    private static readonly object _pluginCommandsLock = new();

    /// <summary>启动钩子表：key = $"{pluginId}.{hookId}"，value = (phase, handler)</summary>
    private static readonly Dictionary<string, (GameLaunchPhase phase, Action<GameLaunchHookContext> handler)> _launchHooks = new();

    public static Action<string, string, string, string?, object?>? OnTabRegistered { get; set; }

    public static Action<string, string, object?, object?>? OnTabRegisteredWithContent { get; set; }

    public static Action<string, string>? OnTabUnregistered { get; set; }

    public static Action<string, string, string, string?, string?, object?>? OnHomeCardRegistered { get; set; }

    public static Action<string>? OnHomeCardUnregistered { get; set; }

    public static Func<string, string, string, int?, string>? OnShowNotification { get; set; }

    public static Action<string, string, double?>? OnUpdateNotification { get; set; }

    public static Action<string>? OnCloseNotification { get; set; }

    /// <summary>日志写入回调（由 Desktop 层注入 DebugLogger）；参数：pluginId, level, message</summary>
    public static Action<string, PluginLogLevel, string>? OnLogMessage { get; set; }

    /// <summary>获取已安装版本列表回调；参数：pluginId，返回版本信息列表</summary>
    public static Func<string, IReadOnlyList<PluginVersionInfo>>? OnGetInstalledVersions { get; set; }

    /// <summary>获取当前账户回调；返回 null 表示未选中账户</summary>
    public static Func<PluginAccountInfo?>? OnGetCurrentAccount { get; set; }

    /// <summary>提交下载请求回调；返回任务ID，空字符串表示被拒绝</summary>
    public static Func<string, PluginDownloadRequest, string>? OnRequestDownload { get; set; }

    public PluginContext(string pluginId)
    {
        _pluginId = pluginId;

        _pluginDataDir = Path.Combine(
            VersionInfo.GetAppBaseDirectory(),
            "OMCL",
            "plugins",
            pluginId
        );
    }

    public string LauncherVersion => "1.0.1";

    public string PluginDataDirectory => _pluginDataDir;

    public void RegisterTab(string title, string tabId, string? icon = null, object? payload = null)
    {
        OnTabRegistered?.Invoke(_pluginId, title, tabId, icon, payload);
    }

    public void RegisterTab(string title, string tabId, object? customContent, string? icon = null, object? payload = null)
    {
        OnTabRegisteredWithContent?.Invoke(title, tabId, customContent, payload);
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
                    DebugLogger.Error("PluginContext", $"事件处理器异常: {ex.Message}");
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

    public void RegisterCommand(string commandId, Action<object?> handler)
    {
        var fullCommandId = $"{_pluginId}.{commandId}";
        lock (_pluginCommandsLock)
        {
            _pluginCommands[fullCommandId] = handler;
        }
    }

    public void UnregisterCommand(string commandId)
    {
        var fullCommandId = $"{_pluginId}.{commandId}";
        lock (_pluginCommandsLock)
        {
            _pluginCommands.Remove(fullCommandId);
        }
    }

    public void LogMessage(PluginLogLevel level, string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            OnLogMessage?.Invoke(_pluginId, level, message);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"插件日志回调异常: {ex.Message}");
        }
    }

    public IReadOnlyList<PluginVersionInfo> GetInstalledVersions()
    {
        try
        {
            return OnGetInstalledVersions?.Invoke(_pluginId) ?? Array.Empty<PluginVersionInfo>();
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"获取已安装版本列表异常: {ex.Message}");
            return Array.Empty<PluginVersionInfo>();
        }
    }

    public PluginAccountInfo? GetCurrentAccount()
    {
        try
        {
            return OnGetCurrentAccount?.Invoke();
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"获取当前账户异常: {ex.Message}");
            return null;
        }
    }

    public void RegisterGameLaunchHook(string hookId, GameLaunchPhase phase, Action<GameLaunchHookContext> handler)
    {
        if (string.IsNullOrEmpty(hookId) || handler == null) return;
        var fullId = $"{_pluginId}.{hookId}";
        _launchHooks[fullId] = (phase, handler);
    }

    public void UnregisterGameLaunchHook(string hookId)
    {
        if (string.IsNullOrEmpty(hookId)) return;
        var fullId = $"{_pluginId}.{hookId}";
        _launchHooks.Remove(fullId);
    }

    public string RequestDownload(PluginDownloadRequest request)
    {
        if (request == null) return string.Empty;
        if (string.IsNullOrWhiteSpace(request.Url)) return string.Empty;
        if (string.IsNullOrWhiteSpace(request.FileName)) return string.Empty;
        if (string.IsNullOrWhiteSpace(request.TargetDirectory)) return string.Empty;

        // 仅允许 http/https 协议，避免 file:/// 等敏感协议
        if (!request.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !request.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // 拒绝文件名包含路径分隔符（防止路径遍历）
        if (request.FileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
        {
            return string.Empty;
        }

        try
        {
            return OnRequestDownload?.Invoke(_pluginId, request) ?? string.Empty;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"提交下载请求异常: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 执行插件注册的自定义命令
    /// </summary>
    /// <param name="fullCommandId">完整命令ID（pluginId.commandId）</param>
    /// <param name="payload">附加数据</param>
    /// <returns>是否找到并执行了命令</returns>
    public static bool ExecuteCommand(string fullCommandId, object? payload)
    {
        Action<object?>? handler = null;
        lock (_pluginCommandsLock)
        {
            if (_pluginCommands.TryGetValue(fullCommandId, out var h))
            {
                handler = h;
            }
        }

        if (handler != null)
        {
            try
            {
                handler(payload);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginContext", $"命令执行异常 [{fullCommandId}]: {ex.Message}");
            }
        }
        return false;
    }

    /// <summary>
    /// 移除指定插件的所有命令（插件卸载时调用）
    /// </summary>
    /// <param name="pluginId">插件ID</param>
    public static void RemovePluginCommands(string pluginId)
    {
        var prefix = $"{pluginId}.";
        var keysToRemove = new List<string>();
        lock (_pluginCommandsLock)
        {
            foreach (var key in _pluginCommands.Keys)
            {
                if (key.StartsWith(prefix))
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
            {
                _pluginCommands.Remove(key);
            }
        }
    }

    /// <summary>
    /// 触发指定阶段的启动钩子。返回所有处理器合并后的最终上下文。
    /// </summary>
    /// <param name="phase">触发阶段</param>
    /// <param name="context">初始上下文（通常由启动器构造）</param>
    /// <returns>经过所有钩子处理后的上下文</returns>
    public static GameLaunchHookContext TriggerGameLaunchHooks(GameLaunchPhase phase, GameLaunchHookContext context)
    {
        if (context == null) return new GameLaunchHookContext();

        // 按 key 字典序触发，保证多次调用顺序稳定
        foreach (var kvp in _launchHooks.OrderBy(k => k.Key))
        {
            if (kvp.Value.phase != phase) continue;
            try
            {
                kvp.Value.handler(context);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginContext", $"启动钩子异常 [{kvp.Key}] ({phase}): {ex.Message}");
            }

            // BeforeLaunch 阶段被某个钩子取消，则不再调用后续 BeforeLaunch 钩子
            if (phase == GameLaunchPhase.BeforeLaunch && context.CancelLaunch) break;
        }

        return context;
    }

    /// <summary>
    /// 获取当前已注册的启动钩子数量（主要用于测试与诊断）
    /// </summary>
    public static int GetRegisteredHookCount() => _launchHooks.Count;

    /// <summary>
    /// 移除指定插件的所有启动钩子（插件卸载时调用）
    /// </summary>
    public static void RemovePluginLaunchHooks(string pluginId)
    {
        var prefix = $"{pluginId}.";
        var keysToRemove = _launchHooks.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
        {
            _launchHooks.Remove(key);
        }
    }

    /// <summary>
    /// 清除启动钩子静态状态（仅用于单元测试隔离，不应在生产代码调用）
    /// 注意：仅清除新增的 _launchHooks，不清除 _eventHandlers / _pluginCommands
    /// 以避免影响并行运行的其他测试类
    /// </summary>
    public static void ClearAllStateForTests()
    {
        _launchHooks.Clear();
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
                    DebugLogger.Error("PluginContext", $"全局事件处理器异常: {ex.Message}");
                }
            }
        }
    }
}
