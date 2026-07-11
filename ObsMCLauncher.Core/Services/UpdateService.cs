using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;
using Velopack;

namespace ObsMCLauncher.Core.Services;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>
/// 更新检查结果
/// </summary>
public class UpdateCheckResult
{
    /// <summary>
    /// 是否有新版本
    /// </summary>
    public bool HasUpdate { get; init; }

    /// <summary>
    /// 新版本号
    /// </summary>
    public string Version { get; init; } = "";

    /// <summary>
    /// 更新说明
    /// </summary>
    public string ReleaseNotes { get; init; } = "";

    /// <summary>
    /// Velopack更新信息，不为null时可通过Velopack自动更新
    /// </summary>
    public UpdateInfo? VelopackUpdateInfo { get; init; }

    /// <summary>
    /// GitHub Release信息，Velopack不可用时用于打开下载页
    /// </summary>
    public GitHubRelease? GitHubRelease { get; init; }

    /// <summary>
    /// 是否可以通过Velopack自动更新
    /// </summary>
    public bool CanAutoUpdate => VelopackUpdateInfo != null;
}

public static class UpdateService
{
    private static readonly HttpClient _httpClient;
    private static UpdateManager? _updateManager;

    private const string GITHUB_OWNER = "mcobs";
    private const string GITHUB_REPO = "ObsMCLauncher";
    private const string GITHUB_REPO_URL = "https://github.com/mcobs/ObsMCLauncher";
    private const string GITHUB_API = "https://api.github.com/repos/{0}/{1}/releases/latest";

    static UpdateService()
    {
        _httpClient = HttpClientFactory.CreateClient(timeout: TimeSpan.FromSeconds(30));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", Utils.VersionInfo.UserAgent);
    }

    /// <summary>
    /// 初始化Velopack UpdateManager（应在应用启动后调用一次）
    /// </summary>
    public static void Initialize()
    {
        try
        {
            var source = new GitHubProxyVelopackSource(GITHUB_REPO_URL, null, false);
            _updateManager = new UpdateManager(source);
            DebugLogger.Info("Update", "Velopack UpdateManager 初始化完成");
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("Update", $"Velopack初始化失败，将使用降级更新方式: {ex.Message}");
            _updateManager = null;
        }
    }

    /// <summary>
    /// 检查更新，优先使用Velopack，失败时降级到GitHub API
    /// </summary>
    public static async Task<UpdateCheckResult?> CheckForUpdatesAsync(bool includePrerelease = false)
    {
        DebugLogger.Info("Update", "开始检查更新...");

        // 优先尝试Velopack
        if (_updateManager != null)
        {
            try
            {
                var updateInfo = await _updateManager.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    var version = updateInfo.TargetFullRelease.Version.ToString();
                    DebugLogger.Info("Update", $"Velopack发现新版本: {version}");

                    return new UpdateCheckResult
                    {
                        HasUpdate = true,
                        Version = version,
                        ReleaseNotes = "",
                        VelopackUpdateInfo = updateInfo,
                        GitHubRelease = null
                    };
                }
                else
                {
                    DebugLogger.Info("Update", "Velopack检查完成，当前已是最新版本");
                    return null;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("Update", $"Velopack检查更新失败，降级到GitHub API: {ex.Message}");
            }
        }

        // 降级到自研GitHub API检查
        return await CheckForUpdatesViaGitHubApiAsync(includePrerelease);
    }

    /// <summary>
    /// 通过Velopack下载并应用更新
    /// </summary>
    public static async Task DownloadAndApplyUpdateAsync(UpdateInfo updateInfo, Action<int>? progress = null)
    {
        if (_updateManager == null)
            throw new InvalidOperationException("UpdateManager未初始化");

        await _updateManager.DownloadUpdatesAsync(updateInfo, progress);
        _updateManager.ApplyUpdatesAndRestart(updateInfo);
    }

    /// <summary>
    /// 打开GitHub Release下载页面
    /// </summary>
    public static void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Update", $"打开Release页面失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 打开默认的GitHub Releases页面
    /// </summary>
    public static void OpenLatestReleasePage()
    {
        OpenReleasePage("https://github.com/mcobs/ObsMCLauncher/releases/latest");
    }

    // ---- 以下为降级方案：自研GitHub API检查 ----

    private static async Task<UpdateCheckResult?> CheckForUpdatesViaGitHubApiAsync(bool includePrerelease)
    {
        try
        {
            string apiUrl = string.Format(GITHUB_API, GITHUB_OWNER, GITHUB_REPO);

            if (includePrerelease)
            {
                apiUrl = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases";
            }

            // API请求不使用代理
            DebugLogger.Info("Update", $"API URL: {apiUrl}");

            var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            GitHubRelease? latestRelease;
            if (includePrerelease)
            {
                var releases = JsonSerializer.Deserialize<GitHubRelease[]>(json);
                latestRelease = releases?.Length > 0 ? releases[0] : null;
            }
            else
            {
                latestRelease = JsonSerializer.Deserialize<GitHubRelease>(json);
            }

            if (latestRelease == null)
            {
                DebugLogger.Warn("Update", "未找到发布版本");
                return null;
            }

            DebugLogger.Info("Update", $"GitHub API最新版本: {latestRelease.TagName}");

            if (IsNewerVersion(latestRelease.TagName, Utils.VersionInfo.ShortVersion))
            {
                DebugLogger.Info("Update", "发现新版本（降级模式）");
                return new UpdateCheckResult
                {
                    HasUpdate = true,
                    Version = latestRelease.TagName,
                    ReleaseNotes = latestRelease.Body ?? "",
                    VelopackUpdateInfo = null,
                    GitHubRelease = latestRelease
                };
            }
            else
            {
                DebugLogger.Info("Update", "当前已是最新版本");
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            DebugLogger.Error("Update", $"网络错误: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("Update", $"检查更新失败: {ex.Message}");
            return null;
        }
    }

    internal static bool IsNewerVersion(string newVersion, string currentVersion)
    {
        try
        {
            newVersion = newVersion.TrimStart('v', 'V');
            currentVersion = currentVersion.TrimStart('v', 'V');

            // 先提取预发布信息，再把版本号中的预发布后缀去掉
            var newPreRelease = GetPreReleaseInfo(newVersion);
            var currentPreRelease = GetPreReleaseInfo(currentVersion);

            var newBase = newPreRelease != null
                ? newVersion.Substring(0, newVersion.IndexOf('-'))
                : newVersion;
            var currentBase = currentPreRelease != null
                ? currentVersion.Substring(0, currentVersion.IndexOf('-'))
                : currentVersion;

            var newParts = newBase.Split('.');
            var currentParts = currentBase.Split('.');

            int maxLength = Math.Max(newParts.Length, currentParts.Length);

            for (int i = 0; i < maxLength; i++)
            {
                var newPartStr = i < newParts.Length ? newParts[i] : "0";
                var currentPartStr = i < currentParts.Length ? currentParts[i] : "0";

                var newPart = int.TryParse(newPartStr, out int np) ? np : 0;
                var currentPart = int.TryParse(currentPartStr, out int cp) ? cp : 0;

                if (newPart > currentPart)
                    return true;
                if (newPart < currentPart)
                    return false;
            }

            // 数字部分完全相等时，比较预发布标记
            if (newPreRelease == null && currentPreRelease != null)
                return true;
            if (newPreRelease != null && currentPreRelease == null)
                return false;

            if (newPreRelease != null && currentPreRelease != null)
            {
                if (newPreRelease.Value.Type != currentPreRelease.Value.Type)
                {
                    var typeOrder = new[] { "alpha", "beta", "rc", "preview" };
                    var newTypeIndex = Array.IndexOf(typeOrder, newPreRelease.Value.Type);
                    var currentTypeIndex = Array.IndexOf(typeOrder, currentPreRelease.Value.Type);

                    if (newTypeIndex > currentTypeIndex)
                        return true;
                    if (newTypeIndex < currentTypeIndex)
                        return false;
                }

                if (newPreRelease.Value.Number > currentPreRelease.Value.Number)
                    return true;
                if (newPreRelease.Value.Number < currentPreRelease.Value.Number)
                    return false;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    internal static int ParseVersionPart(string part)
    {
        var dashIndex = part.IndexOf('-');
        if (dashIndex >= 0)
        {
            part = part.Substring(0, dashIndex);
        }
        return int.TryParse(part, out int result) ? result : 0;
    }

    internal static (string Type, int Number)? GetPreReleaseInfo(string version)
    {
        var dashIndex = version.IndexOf('-');
        if (dashIndex < 0)
            return null;

        var preRelease = version.Substring(dashIndex + 1).ToLowerInvariant();

        var match = System.Text.RegularExpressions.Regex.Match(preRelease, @"(alpha|beta|rc|preview|pre|test)[.\-]?(\d+)?");
        if (match.Success)
        {
            var type = match.Groups[1].Value;
            if (type == "pre") type = "preview";
            if (type == "test") type = "alpha";

            var numberStr = match.Groups[2].Value;
            var number = int.TryParse(numberStr, out int n) ? n : 0;

            return (type, number);
        }

        return (preRelease, 0);
    }
}
