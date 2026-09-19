using System;
using System.Collections.ObjectModel;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 向导「游戏」页：游戏文件夹位置、版本隔离与内存分配。
/// Java 路径不在这里设——它需要扫描本机运行时，属于主界面设置页的事；
/// 首次启动时由自动模式兜底，用户进主界面后可以再改。
/// </summary>
public partial class WelcomeGamePageViewModel : WelcomeStepViewModel
{
    public WelcomeGamePageViewModel(WelcomeViewModel owner)
        : base(owner, "游戏", "决定游戏数据放在哪里、给游戏多少内存。")
    {
        GameDirectoryLocationOptions = new ObservableCollection<DirectoryLocation>(
            (DirectoryLocation[])Enum.GetValues(typeof(DirectoryLocation)));
        GameDirectoryTypeOptions = new ObservableCollection<GameDirectoryType>(
            (GameDirectoryType[])Enum.GetValues(typeof(GameDirectoryType)));
    }

    private LauncherConfig Config => Owner.Config;

    public ObservableCollection<DirectoryLocation> GameDirectoryLocationOptions { get; }

    public ObservableCollection<GameDirectoryType> GameDirectoryTypeOptions { get; }

    /// <summary>游戏文件夹位置：默认（AppData）/ 运行目录 / 自定义</summary>
    public DirectoryLocation GameDirectoryLocation
    {
        get => Config.GameDirectoryLocation;
        set
        {
            if (Config.GameDirectoryLocation == value) return;
            Config.GameDirectoryLocation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomGameDirectory));
            OnPropertyChanged(nameof(GameDirectoryDisplay));
        }
    }

    /// <summary>是否选了自定义位置（决定路径输入行的显隐）</summary>
    public bool IsCustomGameDirectory => GameDirectoryLocation == DirectoryLocation.Custom;

    /// <summary>自定义游戏文件夹路径</summary>
    public string CustomGameDirectory
    {
        get => Config.CustomGameDirectory;
        set
        {
            if (Config.CustomGameDirectory == value) return;
            Config.CustomGameDirectory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GameDirectoryDisplay));
        }
    }

    /// <summary>当前生效的游戏文件夹（给用户看一眼数据到底落在哪）</summary>
    public string GameDirectoryDisplay => Config.GameDirectory;

    /// <summary>版本隔离：关闭时所有版本共用 mods / saves 等文件夹</summary>
    public GameDirectoryType GameDirectoryType
    {
        get => Config.GameDirectoryType;
        set
        {
            if (Config.GameDirectoryType == value) return;
            Config.GameDirectoryType = value;
            OnPropertyChanged();
        }
    }

    /// <summary>最大内存（MB）</summary>
    public int MaxMemory
    {
        get => Config.MaxMemory;
        set
        {
            var clamped = Math.Clamp(value, 512, 1048576);
            if (Config.MaxMemory == clamped) return;
            Config.MaxMemory = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>自定义游戏文件夹的回填（由视图的文件夹选择器调用）</summary>
    public void SetCustomGameDirectory(string path)
    {
        CustomGameDirectory = path;
    }
}
