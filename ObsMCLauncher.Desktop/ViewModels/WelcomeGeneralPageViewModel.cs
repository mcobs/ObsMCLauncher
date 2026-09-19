using System;
using System.Collections.ObjectModel;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 向导「通用」页：更新与安全相关的几个开关。
/// 只写配置，不需要即时预览——这些开关在向导里没有可观察的视觉反馈。
/// </summary>
public partial class WelcomeGeneralPageViewModel : WelcomeStepViewModel
{
    public WelcomeGeneralPageViewModel(WelcomeViewModel owner)
        : base(owner, "通用", "关于更新与安全的基础设置。")
    {
        UpdateChannelOptions = new ObservableCollection<UpdateChannel>(
            (UpdateChannel[])Enum.GetValues(typeof(UpdateChannel)));
    }

    private LauncherConfig Config => Owner.Config;

    /// <summary>可选更新通道</summary>
    public ObservableCollection<UpdateChannel> UpdateChannelOptions { get; }

    /// <summary>启动时自动检查更新</summary>
    public bool AutoCheckUpdate
    {
        get => Config.AutoCheckUpdate;
        set
        {
            if (Config.AutoCheckUpdate == value) return;
            Config.AutoCheckUpdate = value;
            OnPropertyChanged();
        }
    }

    /// <summary>更新通道（正式版 / 测试版 / 预发布版 / 预览版）</summary>
    public UpdateChannel SelectedUpdateChannel
    {
        get => Config.UpdateChannel;
        set
        {
            if (Config.UpdateChannel == value) return;
            Config.UpdateChannel = value;
            OnPropertyChanged();
        }
    }

    /// <summary>下载后校验文件哈希</summary>
    public bool EnableFileHashVerification
    {
        get => Config.EnableFileHashVerification;
        set
        {
            if (Config.EnableFileHashVerification == value) return;
            Config.EnableFileHashVerification = value;
            OnPropertyChanged();
        }
    }

    /// <summary>跳过 SSL 证书验证（网络环境异常时的兜底）</summary>
    public bool SkipSslValidation
    {
        get => Config.SkipSslValidation;
        set
        {
            if (Config.SkipSslValidation == value) return;
            Config.SkipSslValidation = value;
            OnPropertyChanged();
        }
    }
}
