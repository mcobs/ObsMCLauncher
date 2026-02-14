using System.Text.Json.Serialization;

namespace ObsMCLauncher.Core.Plugins;

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

    [JsonPropertyName("entry")]
    public string? Entry { get; set; }

    [JsonPropertyName("minLauncherVersion")]
    public string? MinLauncherVersion { get; set; }

    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; set; } = [];

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
