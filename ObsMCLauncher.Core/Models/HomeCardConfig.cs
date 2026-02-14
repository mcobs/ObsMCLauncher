namespace ObsMCLauncher.Core.Models;

public class HomeCardConfig
{
    public string CardId { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int Order { get; set; } = 0;

    public bool IsPluginCard { get; set; }

    public string? PluginId { get; set; }
}
