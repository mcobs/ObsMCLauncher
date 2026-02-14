using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ObsMCLauncher.Core.Models;

public class HomeCardInfo : INotifyPropertyChanged
{
    private string _cardId = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string? _icon;
    private string? _commandId;
    private object? _payload;
    private bool _isPluginCard;
    private string? _pluginId;
    private bool _isEnabled = true;
    private int _order;

    public string CardId
    {
        get => _cardId;
        set { _cardId = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public string? CommandId
    {
        get => _commandId;
        set { _commandId = value; OnPropertyChanged(); }
    }

    public object? Payload
    {
        get => _payload;
        set { _payload = value; OnPropertyChanged(); }
    }

    public bool IsPluginCard
    {
        get => _isPluginCard;
        set { _isPluginCard = value; OnPropertyChanged(); }
    }

    public string? PluginId
    {
        get => _pluginId;
        set { _pluginId = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public int Order
    {
        get => _order;
        set { _order = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
