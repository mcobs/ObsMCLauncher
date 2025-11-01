using System;

namespace ObsMCLauncher.Plugins
{
    /// <summary>
    /// 启动器插件接口
    /// 所有插件必须实现此接口
    /// </summary>
    public interface ILauncherPlugin
    {
        /// <summary>
        /// 插件唯一标识符
        /// </summary>
        string Id { get; }
        
        /// <summary>
        /// 插件显示名称
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 插件版本
        /// </summary>
        string Version { get; }
        
        /// <summary>
        /// 插件作者
        /// </summary>
        string Author { get; }
        
        /// <summary>
        /// 插件描述
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// 插件加载时调用
        /// </summary>
        /// <param name="context">插件上下文</param>
        void OnLoad(IPluginContext context);
        
        /// <summary>
        /// 插件卸载时调用
        /// </summary>
        void OnUnload();
        
        /// <summary>
        /// 启动器关闭时调用
        /// </summary>
        void OnShutdown();
    }
}

