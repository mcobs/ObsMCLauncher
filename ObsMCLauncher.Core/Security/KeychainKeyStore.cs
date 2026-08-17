using System;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// macOS 登录钥匙串（Keychain）密钥存储，通过 <c>security</c> 命令行访问。
/// 密钥保存在登录钥匙串（login.keychain-db）中，受用户登录口令保护，
/// 与 Windows DPAPI 同级别的安全模型。
/// </summary>
public sealed class KeychainKeyStore : IKeyStore
{
    private const string Service = "ObsMCLauncher";
    private const string Account = "secret-key";

    public bool IsAvailable => OperatingSystem.IsMacOS();

    public byte[] GetKey()
    {
        // 已存在则直接读取（security find 输出为 Base64 密钥）
        var existing = RunSecurity("find-generic-password", $"-a \"{Account}\" -s \"{Service}\" -w");
        if (!string.IsNullOrWhiteSpace(existing))
            return Convert.FromBase64String(existing.Trim());

        // 不存在则生成并写入（-U 允许更新已存在项，幂等）
        var key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);
        RunSecurity("add-generic-password", $"-a \"{Account}\" -s \"{Service}\" -w {encoded} -U");
        return key;
    }

    private static string? RunSecurity(string verb, string arguments)
    {
        var psi = new ProcessStartInfo("security", $"{verb} {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("无法启动 security 命令");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // find 未找到条目时退出码非 0 且无输出，视为"密钥不存在"
        return process.ExitCode == 0 ? output.Trim() : null;
    }
}
