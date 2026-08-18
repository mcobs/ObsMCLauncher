using System;
using System.Security.Cryptography;
using System.Text;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// 机器标识派生密钥（零依赖保底方案）。
/// 密钥 = PBKDF2(机器唯一标识, 固定盐, 600k 迭代, SHA-256) → 32 字节。
/// 机器标识由 用户名 + 机器名 + Home 路径 组合，离开本机即无法解密。
/// </summary>
/// <remarks>
/// 该方案不需要任何密钥文件，跨平台一致；但机器标识熵较低且可被本地探测，
/// 适合作为 <see cref="PlatformKeyStoreFactory"/> 降级链的最终兜底。
/// </remarks>
public sealed class MachineIdKeyStore : IKeyStore
{
    private const int Iterations = 600_000;
    private const int KeySize = 32;

    private static readonly byte[] FixedSalt = Encoding.UTF8.GetBytes("ObsMCLauncher-Sensitive-v1");

    public bool IsAvailable => true;

    public byte[] GetKey()
    {
        var machineId = $"{Environment.UserName}|{Environment.MachineName}|" +
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Rfc2898DeriveBytes.Pbkdf2(
            machineId,
            FixedSalt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }
}
