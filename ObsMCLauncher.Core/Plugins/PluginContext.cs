using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Plugins;

public class PluginContext : IPluginContext
{
    private readonly string _pluginId;
    private readonly string _pluginDataDir;

    private static readonly Dictionary<string, List<(string pluginId, Action<object?> handler)>> _eventHandlers = new();
    private static readonly object _eventHandlersLock = new();

    private static readonly Dictionary<string, Action<object?>> _pluginCommands = new();
    private static readonly object _pluginCommandsLock = new();

    /// <summary>启动钩子表：key = $"{pluginId}.{hookId}"，value = (phase, handler)</summary>
    private static readonly Dictionary<string, (GameLaunchPhase phase, Action<GameLaunchHookContext> handler)> _launchHooks = new();
    private static readonly object _launchHooksLock = new();

    /// <summary>异步启动钩子表：key = $"{pluginId}.{hookId}"，value = (phase, handler)</summary>
    private static readonly Dictionary<string, (GameLaunchPhase phase, Func<GameLaunchHookContext, Task> handler)> _asyncLaunchHooks = new();
    private static readonly object _asyncLaunchHooksLock = new();

    public static Action<string, string, string, string?, object?>? OnTabRegistered { get; set; }

    public static Action<string, string, string, object?, object?>? OnTabRegisteredWithContent { get; set; }

    public static Action<string, string>? OnTabUnregistered { get; set; }

    public static Action<string, string, string, string?, string?, object?, HomeCardSize>? OnHomeCardRegistered { get; set; }

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

    /// <summary>打开外部链接回调；返回是否成功</summary>
    public static Func<string, bool>? OnOpenUrl { get; set; }

    /// <summary>跳转到内部页面回调；参数为目标页面标识</summary>
    public static Action<string>? OnNavigateTo { get; set; }

    /// <summary>查询下载任务状态回调；任务不存在返回 null</summary>
    public static Func<string, PluginDownloadTaskStatus?>? OnGetDownloadTaskStatus { get; set; }

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

    public string LauncherVersion => VersionInfo.Version;

    public string PluginDataDirectory => _pluginDataDir;

    public string LauncherBaseDirectory => VersionInfo.GetAppBaseDirectory();

    public string LauncherDataDirectory => Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL");

    public string GameDirectory => LauncherConfig.Load().GameDirectory;

    public void RegisterTab(string title, string tabId, string? icon = null, object? payload = null)
    {
        OnTabRegistered?.Invoke(_pluginId, title, tabId, icon, payload);
    }

    public void RegisterTab(string title, string tabId, object? customContent, string? icon = null, object? payload = null)
    {
        OnTabRegisteredWithContent?.Invoke(_pluginId, title, tabId, customContent, payload);
    }

    public void UnregisterTab(string tabId)
    {
        OnTabUnregistered?.Invoke(_pluginId, tabId);
    }

    public void SubscribeEvent(string eventName, Action<object?> handler)
    {
        if (string.IsNullOrEmpty(eventName) || handler == null) return;
        lock (_eventHandlersLock)
        {
            if (!_eventHandlers.TryGetValue(eventName, out var list))
            {
                list = new List<(string, Action<object?>)>();
                _eventHandlers[eventName] = list;
            }
            list.Add((_pluginId, handler));
        }
    }

    public void UnsubscribeEvent(string eventName, Action<object?> handler)
    {
        if (string.IsNullOrEmpty(eventName) || handler == null) return;
        lock (_eventHandlersLock)
        {
            if (_eventHandlers.TryGetValue(eventName, out var list))
            {
                list.RemoveAll(e => e.pluginId == _pluginId && e.handler == handler);
                if (list.Count == 0)
                {
                    _eventHandlers.Remove(eventName);
                }
            }
        }
    }

    public void PublishEvent(string eventName, object? eventData)
    {
        List<Action<object?>>? handlers = null;
        lock (_eventHandlersLock)
        {
            if (_eventHandlers.TryGetValue(eventName, out var list))
            {
                handlers = list.Select(e => e.handler).ToList();
            }
        }

        if (handlers == null) return;
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

    public void RegisterHomeCard(
        string cardId,
        string title,
        string description,
        string? icon = null,
        string? commandId = null,
        object? payload = null)
    {
        RegisterHomeCard(cardId, title, description, icon, commandId, payload, HomeCardSize.Medium);
    }

    public void RegisterHomeCard(
        string cardId,
        string title,
        string description,
        string? icon,
        string? commandId,
        object? payload,
        HomeCardSize defaultSize)
    {
        var fullCardId = $"{_pluginId}.{cardId}";
        OnHomeCardRegistered?.Invoke(fullCardId, title, description, icon, commandId, payload, defaultSize);
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
        lock (_launchHooksLock)
        {
            _launchHooks[fullId] = (phase, handler);
        }
    }

    public void UnregisterGameLaunchHook(string hookId)
    {
        if (string.IsNullOrEmpty(hookId)) return;
        var fullId = $"{_pluginId}.{hookId}";
        lock (_launchHooksLock)
        {
            _launchHooks.Remove(fullId);
        }
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

    public PluginDownloadTaskStatus? GetDownloadTaskStatus(string taskId)
    {
        try
        {
            return OnGetDownloadTaskStatus?.Invoke(taskId);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"查询下载任务状态异常: {ex.Message}");
            return null;
        }
    }

    public T? GetConfig<T>()
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"读取插件配置异常: {ex.Message}");
            return default;
        }
    }

    public void SaveConfig<T>(T config)
    {
        try
        {
            Directory.CreateDirectory(_pluginDataDir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetConfigPath(), json);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"保存插件配置异常: {ex.Message}");
        }
    }

    public bool OpenUrl(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            // 仅允许 http/https，避免打开本地或危险协议
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return OnOpenUrl?.Invoke(url) ?? false;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"打开链接异常: {ex.Message}");
            return false;
        }
    }

    public void NavigateTo(string page)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(page)) return;
            OnNavigateTo?.Invoke(page);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("PluginContext", $"导航异常: {ex.Message}");
        }
    }

    private string GetConfigPath() => Path.Combine(_pluginDataDir, "config.json");

    public void RegisterGameLaunchHookAsync(string hookId, GameLaunchPhase phase, Func<GameLaunchHookContext, Task> handler)
    {
        if (string.IsNullOrEmpty(hookId) || handler == null) return;
        var fullId = $"{_pluginId}.{hookId}";
        lock (_asyncLaunchHooksLock)
        {
            _asyncLaunchHooks[fullId] = (phase, handler);
        }
    }

    public void UnregisterGameLaunchHookAsync(string hookId)
    {
        if (string.IsNullOrEmpty(hookId)) return;
        var fullId = $"{_pluginId}.{hookId}";
        lock (_asyncLaunchHooksLock)
        {
            _asyncLaunchHooks.Remove(fullId);
        }
        // 同名的同步钩子一并移除，避免残留
        UnregisterGameLaunchHook(hookId);
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

        List<KeyValuePair<string, (GameLaunchPhase phase, Action<GameLaunchHookContext> handler)>> snapshot;
        lock (_launchHooksLock)
        {
            // 按 key 字典序触发，保证多次调用顺序稳定
            snapshot = _launchHooks.OrderBy(k => k.Key).ToList();
        }

        foreach (var kvp in snapshot)
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
    /// 触发指定阶段的启动钩子（先同步后异步，均按 key 字典序）。返回经过所有钩子处理后的最终上下文。
    /// </summary>
    public static async Task<GameLaunchHookContext> TriggerGameLaunchHooksAsync(GameLaunchPhase phase, GameLaunchHookContext context)
    {
        if (context == null) return new GameLaunchHookContext();

        // 先触发同步钩子
        TriggerGameLaunchHooks(phase, context);
        if (phase == GameLaunchPhase.BeforeLaunch && context.CancelLaunch) return context;

        List<KeyValuePair<string, (GameLaunchPhase phase, Func<GameLaunchHookContext, Task> handler)>> snapshot;
        lock (_asyncLaunchHooksLock)
        {
            snapshot = _asyncLaunchHooks.OrderBy(k => k.Key).ToList();
        }

        foreach (var kvp in snapshot)
        {
            if (kvp.Value.phase != phase) continue;
            try
            {
                await kvp.Value.handler(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("PluginContext", $"异步启动钩子异常 [{kvp.Key}] ({phase}): {ex.Message}");
            }

            if (phase == GameLaunchPhase.BeforeLaunch && context.CancelLaunch) break;
        }

        return context;
    }
    public static int GetRegisteredHookCount()
    {
        lock (_launchHooksLock)
        {
            return _launchHooks.Count;
        }
    }

    /// <summary>
    /// 移除指定插件的所有启动钩子（插件卸载时调用）
    /// </summary>
    public static void RemovePluginLaunchHooks(string pluginId)
    {
        var prefix = $"{pluginId}.";
        List<string> keysToRemove;
        lock (_launchHooksLock)
        {
            keysToRemove = _launchHooks.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var key in keysToRemove)
            {
                _launchHooks.Remove(key);
            }
        }
        lock (_asyncLaunchHooksLock)
        {
            keysToRemove = _asyncLaunchHooks.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var key in keysToRemove)
            {
                _asyncLaunchHooks.Remove(key);
            }
        }
    }

    /// <summary>
    /// 移除指定插件的所有事件订阅（插件卸载时调用）
    /// </summary>
    public static void RemovePluginEventHandlers(string pluginId)
    {
        List<string> keysToRemove;
        lock (_eventHandlersLock)
        {
            keysToRemove = _eventHandlers
                .Where(kv => kv.Value.Any(e => e.pluginId == pluginId))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _eventHandlers[key].RemoveAll(e => e.pluginId == pluginId);
                if (_eventHandlers[key].Count == 0)
                {
                    _eventHandlers.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// 清除启动钩子静态状态（仅用于单元测试隔离，不应在生产代码调用）
    /// 注意：仅清除新增的 _launchHooks，不清除 _eventHandlers / _pluginCommands
    /// 以避免影响并行运行的其他测试类
    /// </summary>
    public static void ClearAllStateForTests()
    {
        lock (_launchHooksLock)
        {
            _launchHooks.Clear();
        }
        lock (_asyncLaunchHooksLock)
        {
            _asyncLaunchHooks.Clear();
        }
    }

    public static void TriggerGlobalEvent(string eventName, object? eventData)
    {
        List<Action<object?>>? handlers = null;
        lock (_eventHandlersLock)
        {
            if (_eventHandlers.TryGetValue(eventName, out var list))
            {
                handlers = list.Select(e => e.handler).ToList();
            }
        }

        if (handlers == null) return;
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
