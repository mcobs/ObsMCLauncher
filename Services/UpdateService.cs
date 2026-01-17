using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using ObsMCLauncher.Models;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// GitHub Release 信息
    /// </summary>
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
    /// 启动器更新服务
    /// </summary>
    public class UpdateService
    {
        private static readonly HttpClient _httpClient;

        // GitHub仓库信息
        private const string GITHUB_OWNER = "mcobs"; // GitHub用户名
        private const string GITHUB_REPO = "ObsMCLauncher"; // 仓库名
        
        // API地址
        private const string GITHUB_API = "https://api.github.com/repos/{0}/{1}/releases/latest";
        
        // 镜像加速地址（用于国内访问）
        private const string GITHUB_MIRROR = "https://gh-proxy.com/"; // GitHub文件加速
        
        /// <summary>
        /// 为GitHub URL添加镜像代理
        /// </summary>
        private static string UseProxyIfNeeded(string url)
        {
            // 如果是GitHub相关的URL，使用镜像源
            if (url.Contains("github.com") || url.Contains("githubusercontent.com"))
            {
                if (!url.StartsWith(GITHUB_MIRROR))
                {
                    Debug.WriteLine($"[UpdateService] 使用镜像源: {GITHUB_MIRROR}");
                    return GITHUB_MIRROR + url;
                }
            }
            return url;
        }
        
        static UpdateService()
        {
            // 配置HttpClient以处理SSL证书问题
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"ObsMCLauncher/{VersionInfo.ShortVersion}");
        }

        /// <summary>
        /// 检查更新
        /// </summary>
        /// <param name="includePrerelease">是否包含预发布版本</param>
        /// <returns>如果有新版本返回Release信息，否则返回null</returns>
        public static async Task<GitHubRelease?> CheckForUpdatesAsync(bool includePrerelease = false)
        {
            try
            {
                Debug.WriteLine("[UpdateService] 开始检查更新...");
                Debug.WriteLine($"[UpdateService] 当前版本: {VersionInfo.ShortVersion}");

                string apiUrl = string.Format(GITHUB_API, GITHUB_OWNER, GITHUB_REPO);
                
                // 如果包含预发布版本，获取所有releases
                if (includePrerelease)
                {
                    apiUrl = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases";
                }

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
                    Debug.WriteLine("[UpdateService] 未找到发布版本");
                    return null;
                }

                Debug.WriteLine($"[UpdateService] 最新版本: {latestRelease.TagName}");

                // 比较版本
                if (IsNewerVersion(latestRelease.TagName, VersionInfo.ShortVersion))
                {
                    Debug.WriteLine("[UpdateService] 发现新版本！");
                    return latestRelease;
                }
                else
                {
                    Debug.WriteLine("[UpdateService] 当前已是最新版本");
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[UpdateService] 网络错误: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] 检查更新失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 比较版本号
        /// </summary>
        /// <param name="newVersion">新版本号 (如 "v1.0.1" 或 "1.0.1")</param>
        /// <param name="currentVersion">当前版本号 (如 "1.0.0")</param>
        /// <returns>如果新版本更高则返回true</returns>
        private static bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                // 移除版本号前的 'v' 字符
                newVersion = newVersion.TrimStart('v', 'V');
                currentVersion = currentVersion.TrimStart('v', 'V');

                // 解析版本号
                var newParts = newVersion.Split('.');
                var currentParts = currentVersion.Split('.');

                int maxLength = Math.Max(newParts.Length, currentParts.Length);

                for (int i = 0; i < maxLength; i++)
                {
                    int newPart = i < newParts.Length && int.TryParse(newParts[i], out int n) ? n : 0;
                    int currentPart = i < currentParts.Length && int.TryParse(currentParts[i], out int c) ? c : 0;

                    if (newPart > currentPart)
                        return true;
                    if (newPart < currentPart)
                        return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 显示更新对话框
        /// </summary>
        public static async Task ShowUpdateDialogAsync(GitHubRelease release)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var result = await DialogManager.Instance.ShowUpdateDialogAsync(
                        $"发现新版本 {release.TagName}",
                        $"**当前版本：** {VersionInfo.DisplayVersion}\n\n" +
                        $"**最新版本：** {release.TagName}\n\n" +
                        $"**发布时间：** {release.PublishedAt:yyyy-MM-dd HH:mm}\n\n" +
                        $"**更新内容：**\n\n{release.Body}",
                        "立即更新",
                        "稍后提醒"
                    );

                    if (result)
                    {
                        // 用户点击了"立即更新"
                        await DownloadUpdateAsync(release);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] 显示更新对话框失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前系统架构
        /// </summary>
        private static string GetCurrentArchitecture()
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            return arch switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "x64" // 默认使用x64
            };
        }

        /// <summary>
        /// 根据架构匹配下载文件
        /// </summary>
        private static GitHubAsset? FindMatchingAsset(GitHubRelease release, string architecture)
        {
            // 优先级匹配规则：
            // 1. 精确匹配架构（如 ObsMCLauncher-1.0.0-x64.exe）
            // 2. 包含架构名称（如 ObsMCLauncher-1.0.0-win-x64.exe）
            // 3. 通用Windows文件（如 ObsMCLauncher-1.0.0.exe，通常默认为x64）
            // 4. 任何.exe或.msi文件（降级方案）

            GitHubAsset? exactMatch = null;
            GitHubAsset? containsMatch = null;
            GitHubAsset? genericMatch = null;
            GitHubAsset? fallbackMatch = null;

            foreach (var asset in release.Assets)
            {
                var name = asset.Name.ToLowerInvariant();
                
                // 必须是Windows可执行文件
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 必须包含Windows相关标识（可选，但推荐）
                if (!name.Contains("win") && !name.Contains("windows") && 
                    !name.Contains(architecture.ToLowerInvariant()) &&
                    !name.Contains("setup") && !name.Contains("installer"))
                {
                    // 如果没有明确标识，可能是通用文件，继续检查
                }

                // 1. 精确匹配：文件名包含架构标识（如 -x64.exe, -x86.exe, -arm64.exe）
                if (name.Contains($"-{architecture.ToLowerInvariant()}.") ||
                    name.Contains($"_{architecture.ToLowerInvariant()}."))
                {
                    exactMatch = asset;
                    Debug.WriteLine($"[UpdateService] 找到精确架构匹配: {asset.Name}");
                    break; // 找到精确匹配，直接返回
                }

                // 2. 包含匹配：文件名包含架构名称（如 win-x64, windows-x64）
                if (name.Contains(architecture.ToLowerInvariant()) && 
                    (name.Contains("win") || name.Contains("windows")))
                {
                    containsMatch ??= asset;
                    Debug.WriteLine($"[UpdateService] 找到包含架构匹配: {asset.Name}");
                }

                // 3. 通用匹配：Windows文件但没有架构标识（通常默认为x64）
                if ((name.Contains("win") || name.Contains("windows") || 
                     name.Contains("setup") || name.Contains("installer")) &&
                    !name.Contains("x86") && !name.Contains("x64") && 
                    !name.Contains("arm") && !name.Contains("arm64"))
                {
                    genericMatch ??= asset;
                    Debug.WriteLine($"[UpdateService] 找到通用Windows文件: {asset.Name}");
                }

                // 4. 降级匹配：任何.exe或.msi文件
                fallbackMatch ??= asset;
            }

            // 返回优先级最高的匹配
            return exactMatch ?? containsMatch ?? genericMatch ?? fallbackMatch;
        }

        /// <summary>
        /// 下载并安装更新
        /// </summary>
        private static async Task DownloadUpdateAsync(GitHubRelease release)
        {
            string? notificationId = null;
            
            try
            {
                // 获取当前系统架构
                var currentArch = GetCurrentArchitecture();
                Debug.WriteLine($"[UpdateService] 当前系统架构: {currentArch}");

                // 根据架构查找匹配的安装包
                var installer = FindMatchingAsset(release, currentArch);

                if (installer == null)
                {
                    Debug.WriteLine("[UpdateService] 未找到安装包，打开Release页面");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NotificationManager.Instance.ShowNotification(
                            "未找到安装包",
                            $"未找到适合 {currentArch} 架构的安装包，正在打开Release页面...",
                            NotificationType.Warning,
                            3
                        );
                    });
                    // 没有找到安装包，打开Release页面
                    await Task.Delay(1000);
                    OpenReleasePage(release.HtmlUrl);
                    return;
                }

                Debug.WriteLine($"[UpdateService] 选择安装包: {installer.Name} (架构: {currentArch})");

                // 使用镜像加速下载（如果URL是GitHub）
                string downloadUrl = UseProxyIfNeeded(installer.BrowserDownloadUrl);

                Debug.WriteLine($"[UpdateService] 开始下载: {installer.Name}");
                Debug.WriteLine($"[UpdateService] 下载地址: {downloadUrl}");
                Debug.WriteLine($"[UpdateService] 文件大小: {installer.Size / 1024.0 / 1024.0:F2} MB");

                // 保存到临时目录
                string tempPath = Path.Combine(Path.GetTempPath(), installer.Name);

                // 显示进度通知
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var fileSizeText = installer.Size > 0 
                        ? $" ({installer.Size / 1024.0 / 1024.0:F2} MB)" 
                        : "";
                    notificationId = NotificationManager.Instance.ShowNotification(
                        "正在下载更新",
                        $"下载文件: {installer.Name}{fileSizeText}",
                        NotificationType.Progress,
                        null
                    );
                });

                try
                {
                    // 下载文件
                    using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;
                    int lastProgress = 0;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;

                        // 更新进度（每5%更新一次，避免频繁更新UI）
                        if (totalBytes > 0)
                        {
                            var progress = (int)(totalRead * 100 / totalBytes);
                            if (progress - lastProgress >= 5 || progress == 100)
                            {
                                lastProgress = progress;
                                var progressText = $"进度: {progress}% ({totalRead / 1024 / 1024:F1}MB / {totalBytes / 1024 / 1024:F1}MB)";
                                
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (notificationId != null)
                                    {
                                        NotificationManager.Instance.UpdateNotification(notificationId, progressText);
                                    }
                                });
                            }
                        }
                    }

                    // 下载完成
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (notificationId != null)
                        {
                            NotificationManager.Instance.UpdateNotification(notificationId, "下载完成，准备安装...");
                        }
                    });

                    Debug.WriteLine($"[UpdateService] 下载完成: {tempPath}");

                    // 延迟1秒
                    await Task.Delay(1000);

                    // 获取当前exe路径
                    var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(currentExePath))
                    {
                        currentExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    }
                    
                    if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath))
                    {
                        throw new Exception("无法获取当前程序路径");
                    }

                    Debug.WriteLine($"[UpdateService] 当前程序路径: {currentExePath}");

                    // 关闭通知（通过显示一个新的成功通知来替代）
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NotificationManager.Instance.ShowNotification(
                            "准备更新",
                            "即将替换程序文件并重启...",
                            NotificationType.Success,
                            2
                        );
                    });

                    // 获取当前exe的文件名（用于重命名）
                    var currentExeFileName = Path.GetFileName(currentExePath);
                    var currentExeDirectory = Path.GetDirectoryName(currentExePath);
                    
                    // 创建更新脚本（批处理文件）
                    var updateScriptPath = Path.Combine(Path.GetTempPath(), $"ObsMCLauncher_Update_{Guid.NewGuid():N}.bat");
                    var scriptContent = $@"@echo off
chcp 65001 >nul
echo 正在更新 ObsMCLauncher...
timeout /t 2 /nobreak >nul

:wait
tasklist /FI ""IMAGENAME eq {currentExeFileName}"" 2>NUL | find /I /N ""{currentExeFileName}"">NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto wait
)

echo 正在替换程序文件...
REM 先删除旧文件（如果存在）
if exist ""{currentExePath}"" (
    del /F /Q ""{currentExePath}"" >nul 2>&1
)

REM 将下载的新版本文件复制并重命名为旧版本的名字
copy /Y ""{tempPath}"" ""{currentExePath}"" >nul
if %ERRORLEVEL% NEQ 0 (
    echo 更新失败！无法替换程序文件。
    pause
    exit /b 1
)

REM 删除临时下载文件
if exist ""{tempPath}"" (
    del /F /Q ""{tempPath}"" >nul 2>&1
)

echo 启动新版本...
start """" ""{currentExePath}""

echo 更新完成！
timeout /t 2 /nobreak >nul
del ""{updateScriptPath}""
";

                    File.WriteAllText(updateScriptPath, scriptContent, Encoding.UTF8);
                    Debug.WriteLine($"[UpdateService] 创建更新脚本: {updateScriptPath}");

                    // 启动更新脚本（隐藏窗口）
                    var scriptProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = updateScriptPath,
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true
                        }
                    };
                    scriptProcess.Start();
                    Debug.WriteLine("[UpdateService] 更新脚本已启动");

                    // 延迟后关闭启动器
                    await Task.Delay(1000);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Application.Current.Shutdown();
                    });
                }
                catch (HttpRequestException ex)
                {
                    Debug.WriteLine($"[UpdateService] 下载失败（网络错误）: {ex.Message}");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NotificationManager.Instance.ShowNotification(
                            "下载失败",
                            "网络连接失败，正在打开浏览器下载...",
                            NotificationType.Warning,
                            3
                        );
                    });

                    // 下载失败，打开浏览器
                    await Task.Delay(1000);
                    OpenReleasePage(release.HtmlUrl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] 下载失败: {ex.Message}");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NotificationManager.Instance.ShowNotification(
                            "下载失败",
                            $"自动下载失败，正在打开浏览器...",
                            NotificationType.Warning,
                            3
                        );
                    });

                    // 下载失败，打开浏览器
                    await Task.Delay(1000);
                    OpenReleasePage(release.HtmlUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] 下载更新失败: {ex.Message}");
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NotificationManager.Instance.ShowNotification(
                        "更新失败",
                        "正在打开浏览器手动下载...",
                        NotificationType.Error,
                        3
                    );
                });

                // 最终降级方案：打开浏览器
                await Task.Delay(1000);
                OpenReleasePage(release.HtmlUrl);
            }
        }

        /// <summary>
        /// 打开Release页面
        /// </summary>
        private static void OpenReleasePage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                
                Debug.WriteLine($"[UpdateService] 已打开Release页面: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] 打开Release页面失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在启动时检查更新
        /// </summary>
        public static async Task CheckUpdateOnStartupAsync()
        {
            var config = LauncherConfig.Load();
            if (!config.AutoCheckUpdate)
            {
                Debug.WriteLine("[UpdateService] 自动检查更新已禁用");
                return;
            }

            Debug.WriteLine("[UpdateService] 启动时检查更新...");
            
            // 延迟3秒后检查（避免影响启动速度）
            await Task.Delay(3000);

            var newRelease = await CheckForUpdatesAsync();
            if (newRelease != null)
            {
                await ShowUpdateDialogAsync(newRelease);
            }
        }

        /// <summary>
        /// 测试更新对话框（调试用）
        /// </summary>
        public static async Task ShowTestUpdateDialogAsync()
        {
            Debug.WriteLine("[UpdateService] 显示测试更新对话框");

            var testRelease = new GitHubRelease
            {
                TagName = "v1.1.0",
                Name = "ObsMCLauncher v1.1.0 - 重大更新",
                Body = @"## 新增功能
# 测试
### 测试

6666
---

**完整更新日志：** https://github.com/mcobs/ObsMCLauncher/releases/tag/v1.1.0

感谢所有贡献者的支持！🎊",
                HtmlUrl = "https://github.com/mcobs/ObsMCLauncher/releases/tag/v1.1.0",
                PublishedAt = DateTime.Now,
                Prerelease = false,
                Assets = new[]
                {
                    new GitHubAsset
                    {
                        Name = "ObsMCLauncher-Setup.exe",
                        BrowserDownloadUrl = "https://github.com/mcobs/ObsMCLauncher/releases/download/v1.1.0/ObsMCLauncher-Setup.exe",
                        Size = 52428800 // 50MB
                    }
                }
            };

            await ShowUpdateDialogAsync(testRelease);
        }
    }
}

