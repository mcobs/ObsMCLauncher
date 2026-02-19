using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

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

public static class UpdateService
{
    private static readonly HttpClient _httpClient;

    private const string GITHUB_OWNER = "mcobs";
    private const string GITHUB_REPO = "ObsMCLauncher";
    private const string GITHUB_API = "https://api.github.com/repos/{0}/{1}/releases/latest";

    static UpdateService()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"ObsMCLauncher/{Utils.VersionInfo.ShortVersion}");
    }

    public static async Task<GitHubRelease?> CheckForUpdatesAsync(bool includePrerelease = false)
    {
        try
        {
            DebugLogger.Info("Update", "开始检查更新...");

            string apiUrl = string.Format(GITHUB_API, GITHUB_OWNER, GITHUB_REPO);

            if (includePrerelease)
            {
                apiUrl = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases";
            }

            // 使用镜像代理
            apiUrl = GitHubProxyHelper.WithProxy(apiUrl);
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

            DebugLogger.Info("Update", $"最新版本: {latestRelease.TagName}");

            if (IsNewerVersion(latestRelease.TagName, Utils.VersionInfo.ShortVersion))
            {
                DebugLogger.Info("Update", "发现新版本！");
                return latestRelease;
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

    private static bool IsNewerVersion(string newVersion, string currentVersion)
    {
        try
        {
            newVersion = newVersion.TrimStart('v', 'V');
            currentVersion = currentVersion.TrimStart('v', 'V');

            var newParts = newVersion.Split('.', 4);
            var currentParts = currentVersion.Split('.', 4);

            int maxLength = Math.Max(newParts.Length, currentParts.Length);

            for (int i = 0; i < maxLength; i++)
            {
                var newPartStr = i < newParts.Length ? newParts[i] : "0";
                var currentPartStr = i < currentParts.Length ? currentParts[i] : "0";

                var newPart = ParseVersionPart(newPartStr);
                var currentPart = ParseVersionPart(currentPartStr);

                if (newPart > currentPart)
                    return true;
                if (newPart < currentPart)
                    return false;
            }

            var newPreRelease = GetPreReleaseInfo(newVersion);
            var currentPreRelease = GetPreReleaseInfo(currentVersion);

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

    private static int ParseVersionPart(string part)
    {
        var dashIndex = part.IndexOf('-');
        if (dashIndex >= 0)
        {
            part = part.Substring(0, dashIndex);
        }
        return int.TryParse(part, out int result) ? result : 0;
    }

    private static (string Type, int Number)? GetPreReleaseInfo(string version)
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

    /// <summary>
    /// 获取适合当前系统的下载资产
    /// </summary>
    public static GitHubAsset? GetAssetForCurrentPlatform(GitHubRelease release)
    {
        if (release.Assets == null || release.Assets.Length == 0) return null;

        var platform = GetPlatformIdentifier();
        DebugLogger.Info("Update", $"当前平台: {platform}");

        foreach (var asset in release.Assets)
        {
            var name = asset.Name.ToLowerInvariant();
            
            if (platform == "win")
            {
                if (name.Contains("win") || name.Contains("windows"))
                {
                    if (Environment.Is64BitOperatingSystem && name.Contains("x64"))
                        return asset;
                    if (!Environment.Is64BitOperatingSystem && name.Contains("x86"))
                        return asset;
                    if (!name.Contains("x86") && !name.Contains("x64") && !name.Contains("arm"))
                        return asset;
                }
            }
            else if (platform == "linux")
            {
                if (name.Contains("linux"))
                    return asset;
            }
            else if (platform == "osx")
            {
                if (name.Contains("osx") || name.Contains("macos") || name.Contains("mac"))
                    return asset;
            }
        }

        return release.Assets[0];
    }

    private static string GetPlatformIdentifier()
    {
        if (OperatingSystem.IsWindows()) return "win";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "unknown";
    }

    /// <summary>
    /// 获取镜像后的下载URL
    /// </summary>
    public static string GetDownloadUrl(GitHubAsset asset)
    {
        return GitHubProxyHelper.WithProxy(asset.BrowserDownloadUrl);
    }

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
}
