using System;
using System.Collections.ObjectModel;
using System.Linq;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 向导「下载」页：下载源、镜像策略、并发数与资源文件。
/// 选项集合的构造口径与设置页保持一致（下载源剔除已弃用的 MCBBS 与自定义）。
/// </summary>
public partial class WelcomeDownloadPageViewModel : WelcomeStepViewModel
{
    public WelcomeDownloadPageViewModel(WelcomeViewModel owner)
        : base(owner, "下载", "选择从哪下载游戏文件，以及下载的并发数。")
    {
        DownloadSourceOptions = new ObservableCollection<DownloadSource>(
            ((DownloadSource[])Enum.GetValues(typeof(DownloadSource)))
                .Where(x => x != DownloadSource.MCBBS && x != DownloadSource.Custom));
        MirrorSourceModeOptions = new ObservableCollection<MirrorSourceMode>(
            (MirrorSourceMode[])Enum.GetValues(typeof(MirrorSourceMode)));
        MaxDownloadThreadsOptions = new ObservableCollection<int> { 4, 8, 16, 32, 64 };
    }

    private LauncherConfig Config => Owner.Config;

    public ObservableCollection<DownloadSource> DownloadSourceOptions { get; }

    public ObservableCollection<MirrorSourceMode> MirrorSourceModeOptions { get; }

    public ObservableCollection<int> MaxDownloadThreadsOptions { get; }

    /// <summary>下载源</summary>
    public DownloadSource DownloadSource
    {
        get => Config.DownloadSource;
        set
        {
            if (Config.DownloadSource == value) return;
            Config.DownloadSource = value;
            OnPropertyChanged();
        }
    }

    /// <summary>镜像源策略</summary>
    public MirrorSourceMode MirrorSourceMode
    {
        get => Config.MirrorSourceMode;
        set
        {
            if (Config.MirrorSourceMode == value) return;
            Config.MirrorSourceMode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>最大下载线程数</summary>
    public int MaxDownloadThreads
    {
        get => Config.MaxDownloadThreads;
        set
        {
            if (Config.MaxDownloadThreads == value) return;
            Config.MaxDownloadThreads = value;
            OnPropertyChanged();
        }
    }

    /// <summary>下载游戏时是否完整下载全部资源文件</summary>
    public bool DownloadAssetsWithGame
    {
        get => Config.DownloadAssetsWithGame;
        set
        {
            if (Config.DownloadAssetsWithGame == value) return;
            Config.DownloadAssetsWithGame = value;
            OnPropertyChanged();
        }
    }
}
