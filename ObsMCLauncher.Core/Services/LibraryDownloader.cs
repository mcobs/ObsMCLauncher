using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

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

        DebugLogger.Info("LibraryDownloader", $"开始下载 {missingLibraryNames.Count} 个缺失的库文件...");

        var versionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(versionJsonPath))
        {
            DebugLogger.Error("LibraryDownloader", $"版本JSON不存在: {versionJsonPath}");
            return (0, missingLibraryNames.Count);
        }

        var versionJson = await File.ReadAllTextAsync(versionJsonPath, cancellationToken);
        using var versionDoc = JsonDocument.Parse(versionJson);
        var root = versionDoc.RootElement;

        // 同一个库名可能要下多份：主 artifact + 本平台的 natives classifier。
        // 只认 downloads.artifact 的话，natives 目录永远是空的，
        // 游戏一起来就 UnsatisfiedLinkError（no lwjgl in java.library.path）。
        var downloadInfos = new List<LibraryDownloadInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 库自己声明的 maven 基址（Fabric 是 https://maven.fabricmc.net/），取不到再退回公共源
        var mavenBaseByLibName = new Dictionary<string, string>(StringComparer.Ordinal);

        void CollectFrom(JsonElement libraryElement)
        {
            if (libraryElement.ValueKind != JsonValueKind.Object)
                return;
            if (!libraryElement.TryGetProperty("name", out var nameElement))
                return;

            var libName = nameElement.GetString();
            if (string.IsNullOrEmpty(libName) || !missingLibraryNames.Contains(libName))
                return;

            // 记下这个库声明的 maven 基址，Maven 兜底时优先用它
            if (!mavenBaseByLibName.ContainsKey(libName) &&
                libraryElement.TryGetProperty("url", out var urlElement) &&
                urlElement.ValueKind == JsonValueKind.String)
            {
                var declaredBase = urlElement.GetString();
                if (!string.IsNullOrEmpty(declaredBase))
                {
                    mavenBaseByLibName[libName] = declaredBase.TrimEnd('/');
                }
            }

            if (!libraryElement.TryGetProperty("downloads", out var downloads) ||
                downloads.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (downloads.TryGetProperty("artifact", out var artifact) &&
                artifact.ValueKind == JsonValueKind.Object)
            {
                AddDownloadInfo(downloadInfos, seenPaths, libName, artifact);
            }

            if (downloads.TryGetProperty("classifiers", out var classifiers) &&
                classifiers.ValueKind == JsonValueKind.Object &&
                libraryElement.TryGetProperty("natives", out var natives) &&
                natives.ValueKind == JsonValueKind.Object &&
                natives.TryGetProperty(GetCurrentOsName(), out var nativesKey) &&
                nativesKey.ValueKind == JsonValueKind.String &&
                classifiers.TryGetProperty(nativesKey.GetString()!, out var classifierArtifact) &&
                classifierArtifact.ValueKind == JsonValueKind.Object)
            {
                AddDownloadInfo(downloadInfos, seenPaths, libName, classifierArtifact);
            }
        }

        if (root.TryGetProperty("libraries", out var librariesElement) &&
            librariesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var lib in librariesElement.EnumerateArray())
            {
                CollectFrom(lib);
            }
        }

        // inheritsFrom：父版本 JSON 可能直接放在子版本目录里（PCL 同款约定）
        if (root.TryGetProperty("inheritsFrom", out var inheritsFromElement) &&
            inheritsFromElement.ValueKind == JsonValueKind.String)
        {
            var parentVersion = inheritsFromElement.GetString();
            if (!string.IsNullOrEmpty(parentVersion))
            {
                DebugLogger.Info("LibraryDownloader", $"检查父版本 {parentVersion} 的库信息...");

                var parentVersionJsonPath = Path.Combine(gameDirectory, "versions", versionId, $"{parentVersion}.json");
                if (File.Exists(parentVersionJsonPath))
                {
                    var parentVersionJson = await File.ReadAllTextAsync(parentVersionJsonPath, cancellationToken);
                    using var parentDoc = JsonDocument.Parse(parentVersionJson);
                    var parentRoot = parentDoc.RootElement;

                    if (parentRoot.TryGetProperty("libraries", out var parentLibrariesElement) &&
                        parentLibrariesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var lib in parentLibrariesElement.EnumerateArray())
                        {
                            CollectFrom(lib);
                        }
                    }
                }
            }
        }

        int successCount = 0;
        int failedCount = 0;
        int current = 0;
        int total = downloadInfos.Count;

        var librariesDir = Path.Combine(gameDirectory, "libraries");

        foreach (var libInfo in downloadInfos)
        {
            current++;
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var destPath = Path.Combine(librariesDir, libInfo.Path.Replace("/", Path.DirectorySeparatorChar.ToString()));
                var destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                progressCallback?.Invoke($"正在下载 {libInfo.Name} ({current}/{total})...", current, total);

                using var response = await _httpClient.GetAsync(libInfo.Url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(destPath, content, cancellationToken);

                    bool hashOk = true;
                    if (FileHashVerifier.IsEnabled && !string.IsNullOrEmpty(libInfo.Sha1))
                    {
                        hashOk = FileHashVerifier.VerifyFileHash(destPath, libInfo.Sha1, HashType.Sha1);
                        if (!hashOk)
                        {
                            DebugLogger.Warn("LibraryDownloader", $"SHA-1校验失败: {libInfo.Name}");
                            File.Delete(destPath);
                        }
                    }

                    if (!hashOk)
                    {
                        failedCount++;
                    }
                    else if (libInfo.Size > 0)
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
        var handledNames = downloadInfos.Select(d => d.Name).Distinct().ToList();
        var missingDownloadInfo = missingLibraryNames.Where(name => !handledNames.Contains(name)).ToList();

        if (missingDownloadInfo.Count > 0)
        {
            foreach (var libName in missingDownloadInfo)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var sources = new List<string>();
                if (mavenBaseByLibName.TryGetValue(libName, out var declaredBase) && !string.IsNullOrEmpty(declaredBase))
                {
                    sources.Add(declaredBase + "/");
                }

                sources.AddRange(new[]
                {
                    "https://libraries.minecraft.net/",
                    "https://bmclapi2.bangbang93.com/maven/",
                    "https://maven.neoforged.net/releases/"
                });

                bool downloaded = false;

                foreach (var baseUrl in sources)
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

                        using var response = await _httpClient.GetAsync(url, cancellationToken);

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

    /// <summary>从一个 `downloads.artifact` / `downloads.classifiers[*]` 节点收集下载信息（按目标路径去重）。</summary>
    private static void AddDownloadInfo(
        List<LibraryDownloadInfo> sink,
        HashSet<string> seenPaths,
        string libName,
        JsonElement artifact)
    {
        if (!artifact.TryGetProperty("url", out var urlElement) ||
            !artifact.TryGetProperty("path", out var pathElement))
        {
            return;
        }

        var url = urlElement.GetString();
        var path = pathElement.GetString();

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(path))
            return;

        if (!seenPaths.Add(path))
            return;

        long size = 0;
        if (artifact.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number)
        {
            size = sizeElement.GetInt64();
        }

        string? sha1 = null;
        if (artifact.TryGetProperty("sha1", out var sha1Element) && sha1Element.ValueKind == JsonValueKind.String)
        {
            sha1 = sha1Element.GetString();
        }

        sink.Add(new LibraryDownloadInfo
        {
            Name = libName,
            Url = url,
            Path = path,
            Size = size,
            Sha1 = sha1
        });
    }

    private static string GetCurrentOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "unknown";
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
        public string? Sha1 { get; set; }
    }
}
