namespace ObsMCLauncher.Core.Plugins;

public class LoadedPlugin
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public string? ReadmePath { get; set; }

    public PluginMetadata? Metadata { get; set; }

    public bool IsLoaded { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorOutput { get; set; }

    public ILauncherPlugin? Instance { get; set; }
}
