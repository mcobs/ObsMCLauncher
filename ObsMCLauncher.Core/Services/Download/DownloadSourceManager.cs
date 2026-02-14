using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Download;

public class DownloadSourceManager
{
    private static DownloadSourceManager? _instance;
    private static readonly object _lock = new();

    private readonly IDownloadSourceService _bmclapi;
    private readonly IDownloadSourceService _mojang;

    private IDownloadSourceService _current;

    public static DownloadSourceManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new DownloadSourceManager();
                }
            }

            return _instance;
        }
    }

    private DownloadSourceManager()
    {
        _bmclapi = new BmclapiDownloadSourceService();
        _mojang = new MojangDownloadSourceService();

        CurrentSource = DownloadSource.BMCLAPI;
        _current = _bmclapi;
    }

    public DownloadSource CurrentSource { get; private set; }

    public IDownloadSourceService CurrentService => _current;

    public void ApplyFromConfig(LauncherConfig config)
    {
        SetDownloadSource(config.DownloadSource);
    }

    public void SetDownloadSource(DownloadSource source)
    {
        CurrentSource = source;
        _current = source switch
        {
            DownloadSource.Official => _mojang,
            DownloadSource.BMCLAPI => _bmclapi,
            _ => _bmclapi
        };
    }
}
