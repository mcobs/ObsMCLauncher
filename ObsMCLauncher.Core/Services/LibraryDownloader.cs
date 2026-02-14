using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 库文件自动下载服务（迁移自 WPF）
/// </summary>
public static class LibraryDownloader
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public static async Task<(int successCount, int failedCount)> DownloadMissingLibrariesAsync(
        string gameDirectory,
        string versionId,
        List<string> missingLibraryNames,
        Action<string, double, double>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (missingLibraryNames == null || missingLibraryNames.Count == 0)
        {
            return (0, 0);
        }

        Debug.WriteLine($"[LibraryDownloader] 开始下载 {missingLibraryNames.Count} 个缺失的库文件...");

        var versionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(versionJsonPath))
        {
            Debug.WriteLine($"[LibraryDownloader] ❌ 版本JSON不存在: {versionJsonPath}");
            return (0, missingLibraryNames.Count);
        }

        var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken);
        var versionDoc = JsonDocument.Parse(versionJson);
        var root = versionDoc.RootElement;

        var libraryDownloadMap = new Dictionary<string, LibraryDownloadInfo>();

        if (root.TryGetProperty("libraries", out var librariesElement))
        {
            foreach (var lib in librariesElement.EnumerateArray())
            {
                if (!lib.TryGetProperty("name", out var nameElement))
                    continue;

                var libName = nameElement.GetString();
                if (string.IsNullOrEmpty(libName))
                    continue;

                if (!missingLibraryNames.Contains(libName))
                    continue;

                if (lib.TryGetProperty("downloads", out var downloads) &&
                    downloads.TryGetProperty("artifact", out var artifact))
                {
                    if (artifact.TryGetProperty("url", out var urlElement) &&
                        artifact.TryGetProperty("path", out var pathElement))
                    {
                        var url = urlElement.GetString();
                        var path = pathElement.GetString();

                        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                        {
                            long size = 0;
                            if (artifact.TryGetProperty("size", out var sizeElement))
                            {
                                size = sizeElement.GetInt64();
                            }

                            libraryDownloadMap[libName] = new LibraryDownloadInfo
                            {
                                Name = libName,
                                Url = url,
                                Path = path,
                                Size = size
                            };
                        }
                    }
                }
            }
        }

        // inheritsFrom
        if (root.TryGetProperty("inheritsFrom", out var inheritsFromElement))
        {
            var parentVersion = inheritsFromElement.GetString();
            if (!string.IsNullOrEmpty(parentVersion))
            {
                Debug.WriteLine($"[LibraryDownloader] 检查父版本 {parentVersion} 的库信息...");

                var parentVersionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{parentVersion}.json");
                if (File.Exists(parentVersionJsonPath))
                {
                    var parentVersionJson = await File.ReadAllTextAsync(parentVersionJsonPath, cancellationToken);
                    var parentDoc = JsonDocument.Parse(parentVersionJson);
                    var parentRoot = parentDoc.RootElement;

                    if (parentRoot.TryGetProperty("libraries", out var parentLibrariesElement))
                    {
                        foreach (var lib in parentLibrariesElement.EnumerateArray())
                        {
                            if (!lib.TryGetProperty("name", out var nameElement))
                                continue;

                            var libName = nameElement.GetString();
                            if (string.IsNullOrEmpty(libName))
                                continue;

                            if (libraryDownloadMap.ContainsKey(libName))
                                continue;

                            if (!missingLibraryNames.Contains(libName))
                                continue;

                            if (lib.TryGetProperty("downloads", out var downloads) &&
                                downloads.TryGetProperty("artifact", out var artifact))
                            {
                                if (artifact.TryGetProperty("url", out var urlElement) &&
                                    artifact.TryGetProperty("path", out var pathElement))
                                {
                                    var url = urlElement.GetString();
                                    var path = pathElement.GetString();

                                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                                    {
                                        long size = 0;
                                        if (artifact.TryGetProperty("size", out var sizeElement))
                                        {
                                            size = sizeElement.GetInt64();
                                        }

                                        libraryDownloadMap[libName] = new LibraryDownloadInfo
                                        {
                                            Name = libName,
                                            Url = url,
                                            Path = path,
                                            Size = size
                                        };
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        int successCount = 0;
        int failedCount = 0;
        int current = 0;
        int total = libraryDownloadMap.Count;

        var librariesDir = Path.Combine(gameDirectory, "libraries");

        foreach (var kvp in libraryDownloadMap)
        {
            current++;
            var libInfo = kvp.Value;

            try
            {
                var destPath = Path.Combine(librariesDir, libInfo.Path.Replace("/", Path.DirectorySeparatorChar.ToString()));
                var destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                progressCallback?.Invoke($"正在下载 {libInfo.Name} ({current}/{total})...", current, total);

                var response = await _httpClient.GetAsync(libInfo.Url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(destPath, content, cancellationToken);

                    if (libInfo.Size > 0)
                    {
                        var fileInfo = new FileInfo(destPath);
                        if (fileInfo.Length == libInfo.Size)
                        {
                            successCount++;
                        }
                        else
                        {
                            failedCount++;
                        }
                    }
                    else
                    {
                        successCount++;
                    }
                }
                else
                {
                    failedCount++;
                }
            }
            catch
            {
                failedCount++;
            }
        }

        // 没下载信息的库：尝试 Maven
        var missingDownloadInfo = missingLibraryNames.Where(name => !libraryDownloadMap.ContainsKey(name)).ToList();

        if (missingDownloadInfo.Count > 0)
        {
            foreach (var libName in missingDownloadInfo)
            {
                var mavenSources = new[]
                {
                    "https://libraries.minecraft.net/",
                    "https://bmclapi2.bangbang93.com/maven/",
                    "https://maven.neoforged.net/releases/"
                };

                bool downloaded = false;

                foreach (var baseUrl in mavenSources)
                {
                    try
                    {
                        var relativePath = MavenCoordinateToPath(libName);
                        var url = baseUrl + relativePath.Replace('\\', '/');
                        var destPath = Path.Combine(librariesDir, relativePath);
                        var destDir = Path.GetDirectoryName(destPath);

                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        var response = await _httpClient.GetAsync(url, cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                            await File.WriteAllBytesAsync(destPath, content, cancellationToken);

                            successCount++;
                            downloaded = true;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (!downloaded)
                {
                    failedCount++;
                }
            }
        }

        return (successCount, failedCount);
    }

    private static string MavenCoordinateToPath(string coordinate)
    {
        var parts = coordinate.Split(':');
        if (parts.Length < 3)
            return coordinate;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? parts[3] : "";
        var extension = "jar";

        if (version.Contains('@'))
        {
            var versionParts = version.Split('@');
            version = versionParts[0];
            extension = versionParts[1];
        }
        else if (classifier.Contains('@'))
        {
            var classifierParts = classifier.Split('@');
            classifier = classifierParts[0];
            extension = classifierParts[1];
        }

        var fileName = string.IsNullOrEmpty(classifier)
            ? $"{artifact}-{version}.{extension}"
            : $"{artifact}-{version}-{classifier}.{extension}";

        return $"{group}/{artifact}/{version}/{fileName}";
    }

    private class LibraryDownloadInfo
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Path { get; set; } = "";
        public long Size { get; set; }
    }
}
