using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ObsMCLauncher.Plugins
{
    /// <summary>
    /// 插件元数据（从plugin.json读取）
    /// </summary>
    public class PluginMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
        
        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("homepage")]
        public string? Homepage { get; set; }
        
        [JsonPropertyName("repository")]
        public string? Repository { get; set; }
        
        [JsonPropertyName("updateUrl")]
        public string? UpdateUrl { get; set; }
        
        [JsonPropertyName("minLauncherVersion")]
        public string MinLauncherVersion { get; set; } = "1.0.0";
        
        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; set; } = new();
        
        [JsonPropertyName("permissions")]
        public List<string> Permissions { get; set; } = new();
    }
    
    /// <summary>
    /// 已加载的插件信息
    /// </summary>
    public class LoadedPlugin
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string? IconPath { get; set; }
        public PluginMetadata Metadata { get; set; } = new();
        public ILauncherPlugin? Instance { get; set; }
        public bool IsLoaded { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorOutput { get; set; }
    }
}

