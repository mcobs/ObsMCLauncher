using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// Linux 桌面 Secret Service（gnome-keyring / KWallet）密钥存储，通过
/// <c>secret-tool</c>（libsecret-tools 包）经 DBus 访问。
/// </summary>
/// <remarks>
/// 依赖 DBus 桌面会话与 secret-tool 命令；无桌面环境时由
/// <see cref="PlatformKeyStoreFactory"/> 降级到 <see cref="FileKeyStore"/>。
/// </remarks>
public sealed class SecretServiceKeyStore : IKeyStore
{
    private const string Service = "obs-mclauncher";
    private const string Key = "secret-key";

    public bool IsAvailable => !OperatingSystem.IsWindows() && CommandExists("secret-tool");

    public byte[] GetKey()
    {
        var existing = RunSecretTool("lookup", $"{Service} {Key}", writeInput: null);
        if (!string.IsNullOrWhiteSpace(existing))
            return Convert.FromBase64String(existing.Trim());

        var key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);
        RunSecretTool("store", $"--label=\"ObsMCLauncher secret key\" {Service} {Key}", writeInput: encoded);
        return key;
    }

    private static string? RunSecretTool(string verb, string arguments, string? writeInput)
    {
        var psi = new ProcessStartInfo("secret-tool", $"{verb} {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = writeInput != null,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("无法启动 secret-tool 命令");

        if (writeInput != null)
        {
            process.StandardInput.Write(writeInput);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // lookup 未找到时退出码非 0 且无输出，视为"密钥不存在"
        return process.ExitCode == 0 ? output.Trim() : null;
    }

    private static bool CommandExists(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (File.Exists(Path.Combine(dir, name))) return true;
        }
        return false;
    }
}
