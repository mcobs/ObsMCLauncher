namespace ObsMCLauncher.Core.Security;

/// <summary>
/// 加密密钥获取策略（"密钥从哪来"）。
/// 各平台实现：macOS Keychain、Linux Secret Service、通用 0600 密钥文件、
/// 以及零依赖的机器标识派生（保底）。
/// </summary>
/// <remarks>
/// 密钥材料统一为 32 字节（AES-256）。获取失败时实现应抛出异常，
/// 由 <see cref="PlatformKeyStoreFactory"/> 的降级链决定回退策略。
/// </remarks>
public interface IKeyStore
{
    /// <summary>当前环境是否可用（静态探测：平台匹配 + 外部依赖存在）。</summary>
    bool IsAvailable { get; }

    /// <summary>获取 32 字节加密密钥。首次调用时可能生成并持久化密钥。</summary>
    byte[] GetKey();
}
