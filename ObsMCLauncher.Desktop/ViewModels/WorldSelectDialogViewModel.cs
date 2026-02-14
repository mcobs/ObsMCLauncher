using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ObsMCLauncher.Desktop.ViewModels;

public partial class WorldSelectDialogViewModel : ObservableObject
{
    public ObservableCollection<WorldItemViewModel> Worlds { get; } = new();

    [ObservableProperty]
    private WorldItemViewModel? selectedWorld;

    public Action<WorldItemViewModel?>? CloseRequested { get; set; }

    public WorldSelectDialogViewModel(string savesDirectory)
    {
        LoadWorlds(savesDirectory);
    }

    private void LoadWorlds(string savesDirectory)
    {
        Worlds.Clear();

        if (string.IsNullOrWhiteSpace(savesDirectory) || !Directory.Exists(savesDirectory))
            return;

        foreach (var dir in Directory.GetDirectories(savesDirectory))
        {
            try
            {
                // Minecraft 世界目录一般包含 level.dat
                var levelDat = Path.Combine(dir, "level.dat");
                if (!File.Exists(levelDat))
                    continue;

                Worlds.Add(new WorldItemViewModel
                {
                    Name = Path.GetFileName(dir),
                    Path = dir
                });
            }
            catch
            {
            }
        }
    }

    [RelayCommand]
    private void Ok()
    {
        CloseRequested?.Invoke(SelectedWorld);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(null);
    }
}

public partial class WorldItemViewModel : ObservableObject
{
    public string Name { get; set; } = "";

    public string Path { get; set; } = "";
}
