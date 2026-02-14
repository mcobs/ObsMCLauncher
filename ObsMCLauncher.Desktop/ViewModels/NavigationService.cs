namespace ObsMCLauncher.Desktop.ViewModels;

public class NavigationService : ViewModelBase
{
    private ViewModelBase? _current;
    public ViewModelBase? Current
    {
        get => _current;
        set
        {
            if (!ReferenceEquals(_current, value))
            {
                _current = value;
                OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Current)));
            }
        }
    }
}
