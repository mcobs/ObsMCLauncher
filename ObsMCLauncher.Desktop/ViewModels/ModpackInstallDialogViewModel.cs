using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class ModpackInstallDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _versionName = "";

    [ObservableProperty]
    private string _prompt = "请输入安装后的版本名称：";

    public Action<string?>? CloseRequested { get; set; }

    public ModpackInstallDialogViewModel(string defaultName)
    {
        VersionName = defaultName;
    }

    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(VersionName))
            return;
        CloseRequested?.Invoke(VersionName);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(null);
    }
}
