using System;
using System.IO;
using ObsMCLauncher.Core.Security;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 平台密钥存储（IKeyStore）测试：
/// - 机器标识派生：确定性、32 字节
/// - 0600 密钥文件：生成/读取一致、损坏文件重新生成
/// - 平台工厂：恒有可用实现且产出 32 字节密钥
/// </summary>
public class KeyStoreTests
{
    [Fact]
    public void MachineIdKeyStore_Returns32Bytes_AndIsStableInProcess()
    {
        var store = new MachineIdKeyStore();

        var key1 = store.GetKey();
        var key2 = store.GetKey();

        Assert.NotNull(key1);
        Assert.Equal(32, key1.Length);
        Assert.Equal(key1, key2); // PBKDF2 确定性派生，进程内一致
        Assert.True(store.IsAvailable);
    }

    [Fact]
    public void FileKeyStore_CreatesKey_ThenReadsBackSameKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obsmc_keystore_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "secret.key");
        try
        {
            var first = new FileKeyStore(path);
            var key1 = first.GetKey();
            Assert.Equal(32, key1.Length);

            // 第二次读取应与首次一致（密钥持久化）
            var second = new FileKeyStore(path);
            Assert.Equal(key1, second.GetKey());
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileKeyStore_InvalidExistingFile_RegeneratesKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obsmc_keystore_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "secret.key");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, new byte[10]); // 长度非法

            var store = new FileKeyStore(path);
            var key = store.GetKey();

            Assert.Equal(32, key.Length);
            Assert.Equal(32, File.ReadAllBytes(path).Length);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PlatformKeyStoreFactory_AlwaysProducesUsableKey()
    {
        var store = PlatformKeyStoreFactory.Create();

        Assert.NotNull(store);
        Assert.True(store.IsAvailable);

        var key = store.GetKey();
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void KeychainKeyStore_OnlyAvailableOnMacOS()
    {
        var store = new KeychainKeyStore();
        Assert.Equal(OperatingSystem.IsMacOS(), store.IsAvailable);
    }

    [Fact]
    public void SecretServiceKeyStore_OnlyAvailableWhenToolPresent()
    {
        var store = new SecretServiceKeyStore();
        // Windows 上必然不可用；Linux 上取决于是否安装了 secret-tool
        Assert.Equal(!OperatingSystem.IsWindows() && HasSecretTool(), store.IsAvailable);
    }

    private static bool HasSecretTool()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (File.Exists(Path.Combine(dir, "secret-tool"))) return true;
        }
        return false;
    }
}
