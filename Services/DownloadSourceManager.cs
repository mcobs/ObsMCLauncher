using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 下载源管理器
    /// </summary>
    public class DownloadSourceManager
    {
        private static DownloadSourceManager? _instance;
        private static readonly object _lock = new object();

        private IDownloadSourceService _currentService;
        private readonly BMCLAPIService _bmclapiService;
        private readonly MojangAPIService _mojangService;

        /// <summary>
        /// 单例实例
        /// </summary>
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

        /// <summary>
        /// 当前下载源服务
        /// </summary>
        public IDownloadSourceService CurrentService => _currentService;

        /// <summary>
        /// 当前下载源
        /// </summary>
        public DownloadSource CurrentSource { get; private set; }

        private DownloadSourceManager()
        {
            _bmclapiService = new BMCLAPIService();
            _mojangService = new MojangAPIService();

            // 默认使用BMCLAPI
            CurrentSource = DownloadSource.BMCLAPI;
            _currentService = _bmclapiService;
        }

        /// <summary>
        /// 设置下载源
        /// </summary>
        public void SetDownloadSource(DownloadSource source)
        {
            CurrentSource = source;
            _currentService = source switch
            {
                DownloadSource.BMCLAPI => _bmclapiService,
                DownloadSource.Mojang => _mojangService,
                _ => _bmclapiService
            };
        }

        /// <summary>
        /// 获取下载源显示名称
        /// </summary>
        public static string GetSourceDisplayName(DownloadSource source)
        {
            return source switch
            {
                DownloadSource.BMCLAPI => "BMCLAPI (推荐-中国大陆)",
                DownloadSource.Mojang => "Mojang 官方源",
                _ => "未知源"
            };
        }

        /// <summary>
        /// 获取下载源描述
        /// </summary>
        public static string GetSourceDescription(DownloadSource source)
        {
            return source switch
            {
                DownloadSource.BMCLAPI => "使用BMCLAPI镜像加速下载，适合中国大陆用户",
                DownloadSource.Mojang => "使用官方源下载，速度可能较慢",
                _ => ""
            };
        }
    }
}

