using System.Windows;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;

namespace ObsMCLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 加载配置并初始化下载源
            var config = LauncherConfig.Load();
            DownloadSourceManager.Instance.SetDownloadSource(config.DownloadSource);
            
            System.Diagnostics.Debug.WriteLine($"启动器已启动，当前下载源: {config.DownloadSource}");
        }
    }
}

