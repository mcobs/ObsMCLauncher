using System;
using System.Security.Cryptography;
using System.Text;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// 敏感数据（账号令牌）静态加密服务。
/// </summary>
/// <remarks>
/// 设计边界：只保护磁盘上的静态存储（accounts.json），内存中保持明文以便业务层
/// 将令牌用于 HTTP 请求 / Java 启动参数。
/// 平台策略：
/// - Windows：DPAPI（<see cref="ProtectedData"/>，CurrentUser 范围），密钥由操作系统 + 当前用户账户管理，
///   即使源码公开也无法在其他机器/用户下解密。
/// - macOS/Linux：AES-256-GCM，密钥由 <see cref="IKeyStore"/> 提供，经
///   <see cref="PlatformKeyStoreFactory"/> 按平台降级选择：
///   macOS 优先登录钥匙串（Keychain），Linux 桌面优先 Secret Service，
///   其次 0600 权限密钥文件，最终兜底机器标识派生（PBKDF2 600k）。
/// 存储格式：密文统一带 "OMCL1:" 前缀；无前缀的值视为旧版明文（向后兼容）。
/// </remarks>
public static class TokenCryptoService
{
    /// <summary>加密载荷版本前缀，用于区分密文与旧版明文，并为未来算法升级预留多版本能力。</summary>
    public const string Prefix = "OMCL1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>平台密钥存储（仅非 Windows 分支使用），进程内只解析一次。</summary>
    private static readonly Lazy<IKeyStore> KeyStore = new(PlatformKeyStoreFactory.Create, isThreadSafe: true);

    /// <summary>
    /// 加密敏感字段。返回带 <see cref="Prefix"/> 前缀的 Base64 密文；
    /// 空值原样返回（null/空串不加密，避免产生无意义密文）。
    /// </summary>
    public static string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        if (OperatingSystem.IsWindows())
        {
            var protectedData = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedData);
        }

        return Prefix + EncryptCore(KeyStore.Value.GetKey(), plaintext);
    }

    /// <summary>
    /// 解密敏感字段。无 <see cref="Prefix"/> 前缀的值视为旧版明文原样返回；
    /// 空值原样返回。认证失败（数据被篡改或密钥不匹配）会抛出异常，由调用方决定降级策略。
    /// </summary>
    public static string? Decrypt(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return cipher;
        if (!cipher.StartsWith(Prefix, StringComparison.Ordinal)) return cipher;

        var payload = cipher[Prefix.Length..];

        if (OperatingSystem.IsWindows())
        {
            var protectedData = ProtectedData.Unprotect(
                Convert.FromBase64String(payload),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(protectedData);
        }

        return DecryptCore(KeyStore.Value.GetKey(), payload);
    }

    /// <summary>
    /// 使用指定密钥执行 AES-256-GCM 加密（内部方法，供单元测试跨平台验证）。
    /// 输出 = Base64(nonce || ciphertext || tag)。
    /// </summary>
    internal static string EncryptCore(byte[] key, string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plain, ciphertext, tag, associatedData: null);

        return Convert.ToBase64String(Concat(nonce, ciphertext, tag));
    }

    /// <summary>
    /// 使用指定密钥执行 AES-256-GCM 解密（内部方法，供单元测试跨平台验证）。
    /// </summary>
    internal static string DecryptCore(byte[] key, string base64Payload)
    {
        var raw = Convert.FromBase64String(base64Payload);
        if (raw.Length < NonceSize + TagSize)
            throw new CryptographicException("加密载荷长度非法");

        var nonce = raw.AsSpan(0, NonceSize);
        var ciphertext = raw.AsSpan(NonceSize, raw.Length - NonceSize - TagSize);
        var tag = raw.AsSpan(raw.Length - TagSize, TagSize);
        var plain = new byte[ciphertext.Length];

        using var gcm = new AesGcm(key, TagSize);
        gcm.Decrypt(nonce, ciphertext, tag, plain, associatedData: null);

        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] Concat(byte[] first, byte[] second, byte[] third)
    {
        var result = new byte[first.Length + second.Length + third.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        Buffer.BlockCopy(third, 0, result, first.Length + second.Length, third.Length);
        return result;
    }
}
