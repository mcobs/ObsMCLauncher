using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using System.Collections.Generic;

namespace ObsMCLauncher.Services
{
    public class MciLmLinkService : IDisposable
    {
        public event EventHandler<int>? ProcessExited;

        private static readonly HttpClient _httpClient = new();
        private const string API_URL = "https://api.shlm.top/mcilm-link/download";
        private const string EXE_NAME_WIN = "MciLm-linkc-windows-{0}.exe";
        private const string EXE_NAME_MAC = "MciLm-linkc-macos-{0}";
        private const string EXE_NAME_LINUX = "MciLm-linkc-linux-{0}";
        private Process? _currentProcess;
        private readonly ConcurrentQueue<string> _outputQueue = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _outputReaderTask;

        public string GetExecutablePath()
        {
            // 运行目录\OMCL\bin
            var binDir = Path.Combine(AppContext.BaseDirectory, "OMCL", "bin");
            Directory.CreateDirectory(binDir);

            string fileName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fileName = string.Format(EXE_NAME_WIN, GetArchitecture());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                fileName = string.Format(EXE_NAME_MAC, GetArchitecture());
            }
            else
            {
                fileName = string.Format(EXE_NAME_LINUX, GetArchitecture());
            }

            return Path.Combine(binDir, fileName);
        }

        public bool IsInstalled()
        {
            var path = GetExecutablePath();
            return File.Exists(path);
        }

        public async Task<bool> DownloadAndInstallAsync(IProgress<string> progress)
        {
            try
            {
                progress.Report("正在获取下载信息...");
                var response = await _httpClient.GetStringAsync(API_URL);
                var result = JsonSerializer.Deserialize<MciLmLinkResponse>(response);
                
                if (result?.Success != true || result.Data == null)
                {
                    throw new Exception("获取下载信息失败");
                }

                string platform = GetPlatform();
                string arch = GetArchitecture();
                string downloadUrl = string.Empty;

                // 查找匹配的下载链接
                var platformData = platform switch
                {
                    "windows" => result.Data.Windows,
                    "macos" => result.Data.MacOS,
                    _ => result.Data.Linux
                };

                Debug.WriteLine($"[MciLmLinkService] 平台: {platform}, 架构: {arch}");
                foreach (var item in platformData)
                {
                    Debug.WriteLine($"[MciLmLinkService] 可用条目 arch: {item.Arch}");
                }

                // API 的 arch 字段是类似 "Windows AMD64" / "Windows ARM64"，这里做归一化匹配
                foreach (var item in platformData)
                {
                    var archText = (item.Arch ?? string.Empty).ToLowerInvariant();

                    bool archMatch = arch switch
                    {
                        "x64" => archText.Contains("amd64") || archText.Contains("x64"),
                        "x86" => archText.Contains("386") || archText.Contains("x86") || archText.Contains("i386"),
                        "arm64" => archText.Contains("arm64"),
                        _ => false
                    };

                    if (!archMatch)
                    {
                        continue;
                    }

                    var file = item.Files.Find(f => (f.Name ?? string.Empty).Contains("命令行"));
                    if (file != null)
                    {
                        downloadUrl = file.Url;
                        Debug.WriteLine($"[MciLmLinkService] 匹配到下载链接: {downloadUrl}");
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception($"未找到匹配的下载链接（platform={platform}, arch={arch}）");
                }

                progress.Report("正在下载 MciLm-link...");
                var exePath = GetExecutablePath();
                var tempPath = exePath + ".download";

                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var httpResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                httpResponse.EnsureSuccessStatusCode();
                
                await httpResponse.Content.CopyToAsync(fileStream);
                await fileStream.FlushAsync();
                fileStream.Close();

                // 设置可执行权限（Linux/macOS）
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetUnixFileMode(tempPath, 
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                // 替换旧版本
                if (File.Exists(exePath))
                {
                    File.Delete(exePath);
                }
                File.Move(tempPath, exePath);

                progress.Report("MciLm-link 安装完成");
                return true;
            }
            catch (Exception ex)
            {
                progress.Report($"下载失败: {ex.Message}");
                return false;
            }
        }

        public bool StartServer(int port, Action<string>? outputCallback = null)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                outputCallback?.Invoke("已有 MciLm-link 进程在运行");
                return false;
            }

            var exePath = GetExecutablePath();
            if (!File.Exists(exePath))
            {
                outputCallback?.Invoke("未找到 MciLm-link 可执行文件");
                return false;
            }

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                while (_outputQueue.TryDequeue(out _)) { }
                _currentProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"-s {port} --parent {Process.GetCurrentProcess().Id}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding = System.Text.Encoding.UTF8
                    },
                    EnableRaisingEvents = true
                };

                _currentProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputQueue.Enqueue(e.Data);
                    }
                };

                _currentProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputQueue.Enqueue($"[ERROR] {e.Data}");
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                // 启动输出处理任务
                _outputReaderTask = Task.Run(async () =>
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        while (_outputQueue.TryDequeue(out var line))
                        {
                            outputCallback?.Invoke(line);
                        }
                        await Task.Delay(100, _cancellationTokenSource.Token);
                    }
                }, _cancellationTokenSource.Token);

                _currentProcess.Exited += (_, __) =>
                {
                    try
                    {
                        ProcessExited?.Invoke(this, _currentProcess.ExitCode);
                    }
                    catch { }
                };

                return true;
            }
            catch (Exception ex)
            {
                outputCallback?.Invoke($"启动 MciLm-link 失败: {ex.Message}");
                return false;
            }
        }

        public bool JoinServer(string code, Action<string>? outputCallback = null)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                outputCallback?.Invoke("已有 MciLm-link 进程在运行");
                return false;
            }

            var exePath = GetExecutablePath();
            if (!File.Exists(exePath))
            {
                outputCallback?.Invoke("未找到 MciLm-link 可执行文件");
                return false;
            }

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                while (_outputQueue.TryDequeue(out _)) { }
                _currentProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"-c {code} --parent {Process.GetCurrentProcess().Id}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding = System.Text.Encoding.UTF8
                    },
                    EnableRaisingEvents = true
                };

                _currentProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputQueue.Enqueue(e.Data);
                    }
                };

                _currentProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputQueue.Enqueue($"[ERROR] {e.Data}");
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                // 启动输出处理任务
                _outputReaderTask = Task.Run(async () =>
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        while (_outputQueue.TryDequeue(out var line))
                        {
                            outputCallback?.Invoke(line);
                        }
                        await Task.Delay(100, _cancellationTokenSource.Token);
                    }
                }, _cancellationTokenSource.Token);

                _currentProcess.Exited += (_, __) =>
                {
                    try
                    {
                        ProcessExited?.Invoke(this, _currentProcess.ExitCode);
                    }
                    catch { }
                };

                return true;
            }
            catch (Exception ex)
            {
                outputCallback?.Invoke($"启动 MciLm-link 失败: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill();
                    _currentProcess.WaitForExit(3000);
                }

                _currentProcess?.Dispose();
                _currentProcess = null;
                while (_outputQueue.TryDequeue(out _)) { }
            }
            catch
            {
                // 忽略停止时的异常
            }
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _currentProcess?.Dispose();
        }

        private static string GetArchitecture()
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "x64"
            };
        }

        private static string GetPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
            return "linux";
        }
    }

    public class MciLmLinkResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public MciLmLinkData? Data { get; set; }
    }

    public class MciLmLinkData
    {
        [JsonPropertyName("windows")]
        public List<MciLmLinkPlatform> Windows { get; set; } = new();

        [JsonPropertyName("macos")]
        public List<MciLmLinkPlatform> MacOS { get; set; } = new();

        [JsonPropertyName("linux")]
        public List<MciLmLinkPlatform> Linux { get; set; } = new();
    }

    public class MciLmLinkPlatform
    {
        [JsonPropertyName("arch")]
        public string Arch { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<MciLmLinkFile> Files { get; set; } = new();
    }

    public class MciLmLinkFile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }
}
