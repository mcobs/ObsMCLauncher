using System;
using System.Collections.Generic;
using System.IO;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Plugins
{
    /// <summary>
    /// 插件上下文实现
    /// </summary>
    public class PluginContext : IPluginContext
    {
        private readonly string _pluginId;
        private readonly string _pluginDataDir;
        
        // 事件系统
        private static readonly Dictionary<string, List<Action<object>>> _eventHandlers = new();
        
        // 插件标签页注册回调
        public static Action<string, string, object, string?>? OnTabRegistered { get; set; }
        
        public PluginContext(string pluginId)
        {
            _pluginId = pluginId;
            
            // 插件数据目录就是插件自己的目录
            // 位于：运行目录/plugins/插件ID/
            // 不自动创建，由开发者根据需要自行创建子目录和文件
            _pluginDataDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "plugins",
                pluginId
            );
        }
        
        public string LauncherVersion => VersionInfo.ShortVersion;
        
        public string PluginDataDirectory => _pluginDataDir;
        
        public NotificationManager NotificationManager => NotificationManager.Instance;
        
        public DialogManager DialogManager => DialogManager.Instance;
        
        public void RegisterTab(string title, object content, string? icon = null)
        {
            OnTabRegistered?.Invoke(_pluginId, title, content, icon);
        }
        
        public void SubscribeEvent(string eventName, Action<object> handler)
        {
            if (!_eventHandlers.ContainsKey(eventName))
            {
                _eventHandlers[eventName] = new List<Action<object>>();
            }
            
            _eventHandlers[eventName].Add(handler);
        }
        
        public void PublishEvent(string eventName, object eventData)
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
        
        /// <summary>
        /// 触发全局事件（由启动器内部调用）
        /// </summary>
        public static void TriggerGlobalEvent(string eventName, object eventData)
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
}

