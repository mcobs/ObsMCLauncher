namespace ObsMCLauncher.Desktop.ViewModels;

public class PlaceholderPageViewModel : ViewModelBase
{
    public PlaceholderPageViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
}
