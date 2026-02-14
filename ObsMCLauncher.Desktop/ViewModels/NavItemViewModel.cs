namespace ObsMCLauncher.Desktop.ViewModels;

public class NavItemViewModel
{
    public NavItemViewModel(string title, ViewModelBase page, string icon = "")
    {
        Title = title;
        Page = page;
        Icon = icon;
    }

    public string Title { get; }
    public ViewModelBase Page { get; }
    public string Icon { get; }
}
