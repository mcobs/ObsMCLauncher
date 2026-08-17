using System;
using System.IO;
using System.Security.Cryptography;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// 权限保护密钥文件方案（Linux 无桌面 / 通用 fallback）。
/// 首次调用生成 32 字节随机密钥，写入 <c>~/.config/ObsMCLauncher/secret.key</c>
/// 并设置 0600 权限（仅当前用户可读写）；后续调用直接读取。
/// </summary>
/// <remarks>
/// 与 gh CLI / AWS CLI / docker 等主流工具的凭据存储模式一致，零外部依赖。
/// 静态安全依赖用户目录/磁盘加密（LUKS、home 加密等）。
/// </remarks>
public sealed class FileKeyStore : IKeyStore
{
    private readonly string _keyFilePath;

    public FileKeyStore(string? keyFilePath = null)
    {
        _keyFilePath = keyFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "ObsMCLauncher", "secret.key");
    }

    public bool IsAvailable => !OperatingSystem.IsWindows();

    public byte[] GetKey()
    {
        var directory = Path.GetDirectoryName(_keyFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(_keyFilePath))
        {
            var existing = File.ReadAllBytes(_keyFilePath);
            if (existing.Length == 32)
                return existing;

            // 文件损坏/长度不符：重新生成（旧密文将无法解密，属异常恢复路径）
            File.Delete(_keyFilePath);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(_keyFilePath, key);

        // 仅当前用户可读写（Windows 上该方案不使用，Unix 上设置 0600）
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_keyFilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return key;
    }
}
