using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public class MciLmLinkService : IDisposable
{
    public event EventHandler<int>? ProcessExited;

    private static readonly HttpClient _httpClient = new();

    static MciLmLinkService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", VersionInfo.UserAgent);
    }
    private const string ApiUrl = "https://api.shlm.top/mcilm-link/download";
    private const string ExeNameWin = "MciLm-linkc-windows-{0}.exe";
    private const string ExeNameMac = "MciLm-linkc-macos-{0}";
    private const string ExeNameLinux = "MciLm-linkc-linux-{0}";

    private Process? _currentProcess;
    private readonly ConcurrentQueue<string> _outputQueue = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _outputReaderTask;

    public string GetExecutablePath()
    {
        var binDir = Path.Combine(VersionInfo.GetAppBaseDirectory(), "OMCL", "bin");
        Directory.CreateDirectory(binDir);

        string fileName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileName = string.Format(ExeNameWin, GetArchitecture());
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            fileName = string.Format(ExeNameMac, GetArchitecture());
        }
        else
        {
            fileName = string.Format(ExeNameLinux, GetArchitecture());
        }

        return Path.Combine(binDir, fileName);
    }

    public bool IsInstalled() => File.Exists(GetExecutablePath());

    public async Task<bool> DownloadAndInstallAsync(IProgress<string> progress)
    {
        var tempPath = "";
        try
        {
            progress.Report("正在获取下载信息...");
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _httpClient.GetStringAsync(ApiUrl, timeoutCts.Token);
            var result = JsonSerializer.Deserialize<MciLmLinkResponse>(response);

            if (result?.Success != true || result.Data == null)
                throw new Exception("获取下载信息失败");

            var platform = GetPlatform();
            var arch = GetArchitecture();

            var platformData = platform switch
            {
                "windows" => result.Data.Windows,
                "macos" => result.Data.MacOS,
                _ => result.Data.Linux
            };

            var downloadUrl = string.Empty;
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
                    continue;

                var file = item.Files.Find(f => (f.Name ?? string.Empty).Contains("命令行"));
                if (file != null)
                {
                    downloadUrl = file.Url;
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
                throw new Exception($"未找到匹配的下载链接（platform={platform}, arch={arch}）");

            progress.Report("正在下载 MciLm-link...");

            var exePath = GetExecutablePath();
            tempPath = exePath + ".download";

            using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var httpResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token))
            {
                httpResponse.EnsureSuccessStatusCode();
                await httpResponse.Content.CopyToAsync(fileStream, downloadCts.Token);
                await fileStream.FlushAsync();
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(
                    tempPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            if (File.Exists(exePath))
                File.Delete(exePath);

            File.Move(tempPath, exePath);

            progress.Report("MciLm-link 安装完成");
            return true;
        }
        catch (Exception ex)
        {
            progress.Report($"下载失败: {ex.Message}");
            return false;
        }
        finally
        {
            // 清理失败中断留下的临时文件
            if (!string.IsNullOrEmpty(tempPath))
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    public bool StartServer(int port, Action<string>? outputCallback = null)
    {
        return StartProcess(args =>
        {
            args.Add("-s");
            args.Add(port.ToString());
            args.Add("--parent");
            args.Add(Process.GetCurrentProcess().Id.ToString());
        }, outputCallback);
    }

    public bool JoinServer(string code, Action<string>? outputCallback = null)
    {
        return StartProcess(args =>
        {
            args.Add("-c");
            args.Add(code);
            args.Add("--parent");
            args.Add(Process.GetCurrentProcess().Id.ToString());
        }, outputCallback);
    }

    private bool StartProcess(Action<System.Collections.ObjectModel.Collection<string>> fillArguments, Action<string>? outputCallback)
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

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            // ArgumentList 自动处理参数转义，防止 code 等用户输入破坏命令行结构
            fillArguments(process.StartInfo.ArgumentList);

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _outputQueue.Enqueue(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _outputQueue.Enqueue($"[ERROR] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _currentProcess = process;

            _outputReaderTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    while (_outputQueue.TryDequeue(out var line))
                        outputCallback?.Invoke(line);

                    await Task.Delay(100, _cancellationTokenSource.Token);
                }
            }, _cancellationTokenSource.Token);

            // 捕获局部引用，避免字段在退出回调前被新进程替换时读到错误数据
            var capturedProcess = process;
            process.Exited += (_, __) =>
            {
                try { ProcessExited?.Invoke(this, capturedProcess.ExitCode); }
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
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource?.Dispose();
        _currentProcess?.Dispose();
    }

    private static string GetArchitecture() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        _ => "x64"
    };

    private static string GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
        return "linux";
    }

    private sealed class MciLmLinkResponse
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

    private sealed class MciLmLinkData
    {
        [JsonPropertyName("windows")]
        public List<MciLmLinkPlatform> Windows { get; set; } = new();

        [JsonPropertyName("macos")]
        public List<MciLmLinkPlatform> MacOS { get; set; } = new();

        [JsonPropertyName("linux")]
        public List<MciLmLinkPlatform> Linux { get; set; } = new();
    }

    private sealed class MciLmLinkPlatform
    {
        [JsonPropertyName("arch")]
        public string? Arch { get; set; }

        [JsonPropertyName("files")]
        public List<MciLmLinkFile> Files { get; set; } = new();
    }

    private sealed class MciLmLinkFile
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }
}
