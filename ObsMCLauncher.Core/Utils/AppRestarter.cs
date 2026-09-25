using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ObsMCLauncher.Core.Utils;

/// <summary>
/// 重启启动器。插件更新、应用更新等"必须重启才生效"的场景统一走这里，
/// 避免各处各写一份进程启动代码。
/// </summary>
public static class AppRestarter
{
    /// <summary>
    /// 拉起一个全新的启动器实例。<b>调用方负责随后退出当前进程</b>（关窗即可），
    /// 这里不主动 Kill，以免和 Avalonia 的窗口生命周期打架。
    /// </summary>
    /// <param name="error">失败原因（成功时为 null）</param>
    /// <returns>是否已成功创建新进程</returns>
    public static bool TryStartNewInstance(out string? error)
    {
        error = null;

        try
        {
            var startInfo = BuildStartInfo();
            if (startInfo == null)
            {
                error = "找不到当前程序的可执行文件";
                return false;
            }

            var process = Process.Start(startInfo);
            if (process == null)
            {
                error = "无法创建新的启动器进程";
                return false;
            }

            DebugLogger.Info("AppRestarter", $"已拉起新实例: {startInfo.FileName} {startInfo.Arguments}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            DebugLogger.Error("AppRestarter", $"重启启动器失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 构造重启命令。
    /// 开发期（<c>dotnet run</c>）下当前进程是 dotnet.exe，直接重启会打开一个空的 dotnet 主机，
    /// 因此这种情况改成 <c>dotnet &lt;程序集&gt;.dll</c>。
    /// </summary>
    private static ProcessStartInfo? BuildStartInfo()
    {
        var processPath = Environment.ProcessPath;
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;

        var isDotnetHost = !string.IsNullOrEmpty(processPath) &&
                           string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);

        if (!isDotnetHost && !string.IsNullOrEmpty(processPath) && File.Exists(processPath))
        {
            return new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }

        if (isDotnetHost && !string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath))
        {
            return new ProcessStartInfo
            {
                FileName = processPath!,
                Arguments = $"\"{assemblyPath}\"",
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }

        return null;
    }
}
