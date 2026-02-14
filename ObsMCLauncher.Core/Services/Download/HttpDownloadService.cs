using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services.Download;

public static class HttpDownloadService
{
    private static readonly HttpClient _httpClient;

    static HttpDownloadService()
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
            Timeout = TimeSpan.FromMinutes(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        _httpClient.DefaultRequestHeaders.ConnectionClose = false;
    }

    public static async Task DownloadFileToPathAsync(string url, string savePath, string taskId, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

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
            if ((now - lastReportTime).TotalMilliseconds >= 200)
            {
                var elapsedSeconds = (now - lastReportTime).TotalSeconds;
                var bytesInPeriod = downloadedBytes - lastReportedBytes;
                var speed = elapsedSeconds > 0 ? bytesInPeriod / elapsedSeconds : 0;

                var progress = totalBytes > 0 ? downloadedBytes * 100.0 / totalBytes : 0;
                DownloadTaskManager.Instance.UpdateTaskProgress(
                    taskId,
                    progress,
                    totalBytes > 0
                        ? $"{downloadedBytes / 1024.0 / 1024.0:F1}MB / {totalBytes / 1024.0 / 1024.0:F1}MB"
                        : $"{downloadedBytes / 1024.0 / 1024.0:F1}MB",
                    speed);

                lastReportTime = now;
                lastReportedBytes = downloadedBytes;
            }
        }

        DownloadTaskManager.Instance.UpdateTaskProgress(taskId, 100, "完成", 0);
    }
}
