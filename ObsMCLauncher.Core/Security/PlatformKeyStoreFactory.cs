using System;
using System.Linq;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// 按平台 + 可用性选择密钥存储策略，形成降级链：
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>macOS：Keychain → 0600 密钥文件 → 机器标识派生</item>
/// <item>Linux/其他：Secret Service（桌面）→ 0600 密钥文件 → 机器标识派生</item>
/// <item>Windows：不参与（走 DPAPI 分支），此处仅作占位兜底</item>
/// </list>
/// 降级是静态探测（平台 + 外部命令存在性），保证无桌面/无依赖环境也能运行。
/// </remarks>
public static class PlatformKeyStoreFactory
{
    public static IKeyStore Create()
    {
        if (OperatingSystem.IsMacOS())
        {
            return FirstAvailable(
                new KeychainKeyStore(),
                new FileKeyStore(),
                new MachineIdKeyStore());
        }

        // Windows 与其余平台：Secret Service（仅 Linux 桌面）→ 文件 → 机器标识
        return FirstAvailable(
            new SecretServiceKeyStore(),
            new FileKeyStore(),
            new MachineIdKeyStore());
    }

    private static IKeyStore FirstAvailable(params IKeyStore[] candidates)
        => candidates.FirstOrDefault(s => s.IsAvailable) ?? new MachineIdKeyStore();
}
