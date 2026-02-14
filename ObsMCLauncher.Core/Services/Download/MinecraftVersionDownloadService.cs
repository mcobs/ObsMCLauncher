using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Download;

public static class MinecraftVersionDownloadService
{
    private static readonly HttpClient _httpClient;

    static MinecraftVersionDownloadService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            MaxConnectionsPerServer = 10,
            UseProxy = true,
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
    }

    public static async Task<string?> DownloadVersionJsonAsync(string versionId, string gameDirectory, string? customVersionName, CancellationToken cancellationToken)
    {
        var config = LauncherConfig.Load();
        DownloadSourceManager.Instance.ApplyFromConfig(config);
        var source = DownloadSourceManager.Instance.CurrentService;

        var installName = string.IsNullOrEmpty(customVersionName) ? versionId : customVersionName;

        var taskCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = DownloadTaskManager.Instance.AddTask($"下载版本JSON: {installName}", DownloadTaskType.Version, taskCts);

        try
        {
            var versionJsonUrl = source.GetVersionJsonUrl(versionId);
            var versionJsonPath = Path.Combine(gameDirectory, "versions", installName, $"{installName}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionJsonPath)!);

            DownloadTaskManager.Instance.UpdateTaskProgress(task.Id, 0, $"请求: {versionJsonUrl}");

            var json = await DownloadStringWithProgressAsync(versionJsonUrl, task.Id, taskCts.Token).ConfigureAwait(false);
            await File.WriteAllTextAsync(versionJsonPath, json, taskCts.Token).ConfigureAwait(false);

            DownloadTaskManager.Instance.CompleteTask(task.Id);
            return versionJsonPath;
        }
        catch (OperationCanceledException)
        {
            DownloadTaskManager.Instance.CancelTask(task.Id);
            throw;
        }
        catch (Exception ex)
        {
            DownloadTaskManager.Instance.FailTask(task.Id, ex.Message);
            return null;
        }
        finally
        {
            taskCts.Dispose();
        }
    }

    public static async Task<bool> DownloadClientJarAsync(string versionId, string gameDirectory, string? customVersionName, CancellationToken cancellationToken)
    {
        var installName = string.IsNullOrEmpty(customVersionName) ? versionId : customVersionName;

        var versionJsonPath = await DownloadVersionJsonAsync(versionId, gameDirectory, customVersionName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(versionJsonPath) || !File.Exists(versionJsonPath))
            return false;

        var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken).ConfigureAwait(false);
        var info = JsonSerializer.Deserialize<VersionInfo>(versionJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (info?.Downloads?.Client?.Url == null)
            return false;

        var clientJarUrl = info.Downloads.Client.Url;

        // BMCLAPI 对 client jar 有专用接口（更快）
        var cfg = LauncherConfig.Load();
        if (cfg.DownloadSource == DownloadSource.BMCLAPI)
        {
            clientJarUrl = $"https://bmclapi2.bangbang93.com/version/{versionId}/client";
        }

        var clientJarPath = Path.Combine(gameDirectory, "versions", installName, $"{installName}.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(clientJarPath)!);

        var taskCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = DownloadTaskManager.Instance.AddTask($"下载客户端JAR: {installName}", DownloadTaskType.Version, taskCts);

        try
        {
            await DownloadFileWithProgressAsync(clientJarUrl, clientJarPath, task.Id, taskCts.Token).ConfigureAwait(false);
            DownloadTaskManager.Instance.CompleteTask(task.Id);
            return true;
        }
        catch (OperationCanceledException)
        {
            DownloadTaskManager.Instance.CancelTask(task.Id);
            throw;
        }
        catch (Exception ex)
        {
            DownloadTaskManager.Instance.FailTask(task.Id, ex.Message);
            return false;
        }
        finally
        {
            taskCts.Dispose();
        }
    }

    private static async Task<string> DownloadStringWithProgressAsync(string url, string taskId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // 对于文本我们也做一个“假进度”：按读取字节累计
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var ms = new MemoryStream();

        var buffer = new byte[16 * 1024];
        long readTotal = 0;
        var lastReport = DateTime.UtcNow;
        long lastBytes = 0;

        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;

            var now = DateTime.UtcNow;
            if ((now - lastReport).TotalMilliseconds >= 150)
            {
                var dt = (now - lastReport).TotalSeconds;
                var speed = dt > 0 ? (readTotal - lastBytes) / dt : 0;
                var progress = totalBytes > 0 ? readTotal * 100.0 / totalBytes : 0;

                DownloadTaskManager.Instance.UpdateTaskProgress(taskId, progress, $"已下载 {readTotal / 1024.0:F1} KB", speed);

                lastReport = now;
                lastBytes = readTotal;
            }
        }

        DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 100, "完成", 0);

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task DownloadFileWithProgressAsync(string url, string savePath, string taskId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long downloadedBytes = 0;

        var lastReportTime = DateTime.UtcNow;
        var lastReportedBytes = 0L;

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            downloadedBytes += bytesRead;

            var now = DateTime.UtcNow;
            if ((now - lastReportTime).TotalMilliseconds >= 150)
            {
                var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                var bytesInPeriod = downloadedBytes - lastReportedBytes;
                var speed = elapsedSeconds > 0 ? bytesInPeriod / elapsedSeconds : 0;

                var progress = totalBytes > 0 ? downloadedBytes * 100.0 / totalBytes : 0;
                DownloadTaskManager.Instance.UpdateTaskProgress(taskId, progress, $"{downloadedBytes / 1024.0 / 1024.0:F1}MB / {totalBytes / 1024.0 / 1024.0:F1}MB", speed);

                lastReportTime = now;
                lastReportedBytes = downloadedBytes;
            }
        }

        DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 100, "完成", 0);
    }

    private class VersionInfo
    {
        [JsonPropertyName("downloads")]
        public DownloadsInfo? Downloads { get; set; }
    }

    private class DownloadsInfo
    {
        [JsonPropertyName("client")]
        public DownloadItem? Client { get; set; }
    }

    private class DownloadItem
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
