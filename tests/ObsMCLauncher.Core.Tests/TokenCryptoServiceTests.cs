using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Security;
using Xunit;

namespace ObsMCLauncher.Core.Tests;

/// <summary>
/// 敏感令牌加密服务测试：
/// - 加解密往返（Windows 走 DPAPI，其他平台走 PBKDF2 + AES-256-GCM）
/// - 旧版明文兼容（无 "OMCL1:" 前缀原样返回）
/// - AES-256-GCM 完整性保护（错误密钥解密失败）
/// - GameAccount 序列化边界：敏感字段加密落盘、非敏感字段明文、反序列化还原
/// </summary>
public class TokenCryptoServiceTests
{
    private static readonly JsonSerializerOptions TestOptions = new()
    {
        WriteIndented = true,
        Converters = { new GameAccountSensitiveConverter() }
    };

    [Fact]
    public void Encrypt_Decrypt_RoundTrip()
    {
        const string token = "M.R3_BAY~secret-token-value-123456";

        var cipher = TokenCryptoService.Encrypt(token);

        Assert.NotNull(cipher);
        Assert.StartsWith("OMCL1:", cipher, StringComparison.Ordinal);
        Assert.NotEqual(token, cipher);
        Assert.DoesNotContain(token, cipher);
        Assert.Equal(token, TokenCryptoService.Decrypt(cipher));
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_UnicodeAndEmpty()
    {
        Assert.Equal("令牌令牌", TokenCryptoService.Decrypt(TokenCryptoService.Encrypt("令牌令牌")));
        Assert.Null(TokenCryptoService.Encrypt(null));
        Assert.Equal(string.Empty, TokenCryptoService.Encrypt(string.Empty));
        Assert.Null(TokenCryptoService.Decrypt(null));
        Assert.Equal(string.Empty, TokenCryptoService.Decrypt(string.Empty));
    }

    [Fact]
    public void Decrypt_LegacyPlaintext_ReturnsAsIs()
    {
        // 旧版 accounts.json 中令牌为明文，无前缀时应原样返回，保证向后兼容
        const string legacy = "legacy-plain-token";
        Assert.Equal(legacy, TokenCryptoService.Decrypt(legacy));
    }

    [Fact]
    public void EncryptCore_SameKey_SamePlaintext_ProducesUniqueCiphertext()
    {
        // 随机 nonce：同一密钥同一明文两次加密结果不同（防止重放/比较攻击）
        var key = new byte[32];
        var c1 = TokenCryptoService.EncryptCore(key, "same-value");
        var c2 = TokenCryptoService.EncryptCore(key, "same-value");

        Assert.NotEqual(c1, c2);
        Assert.Equal("same-value", TokenCryptoService.DecryptCore(key, c1));
        Assert.Equal("same-value", TokenCryptoService.DecryptCore(key, c2));
    }

    [Fact]
    public void DecryptCore_WrongKey_ThrowsAuthenticationError()
    {
        // GCM 认证标签：错误密钥解密必须失败，保证机密性 + 完整性
        var key1 = new byte[32];
        var key2 = new byte[32];
        key2[0] = 0x01;

        var cipher = TokenCryptoService.EncryptCore(key1, "protected-data");

        Assert.ThrowsAny<CryptographicException>(() => TokenCryptoService.DecryptCore(key2, cipher));
    }

    [Fact]
    public void DecryptCore_TamperedPayload_ThrowsAuthenticationError()
    {
        var key = new byte[32];
        var cipher = TokenCryptoService.EncryptCore(key, "original");
        var bytes = Convert.FromBase64String(cipher);
        bytes[bytes.Length - 1] ^= 0xFF; // 篡改认证标签
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => TokenCryptoService.DecryptCore(key, tampered));
    }

    [Fact]
    public void GameAccountConverter_SensitiveFieldsEncrypted_NonSensitivePlain()
    {
        var account = new GameAccount
        {
            Username = "Steve",
            Type = AccountType.Microsoft,
            UUID = "069a79f4-44e9-4726-a5be-fca90e38aaf5",
            MinecraftUUID = "069a79f444e94726a5befca90e38aaf5",
            AccessToken = "microsoft-at",
            RefreshToken = "microsoft-rt",
            MinecraftAccessToken = "minecraft-at",
            YggdrasilAccessToken = "ygg-at",
            YggdrasilClientToken = "ygg-ct",
            SkinUrl = "https://example.com/skin.png"
        };

        var json = JsonSerializer.Serialize(new List<GameAccount> { account }, TestOptions);

        // 敏感字段不得以明文出现，且带加密前缀
        Assert.DoesNotContain("microsoft-at", json);
        Assert.DoesNotContain("microsoft-rt", json);
        Assert.DoesNotContain("minecraft-at", json);
        Assert.DoesNotContain("ygg-at", json);
        Assert.DoesNotContain("ygg-ct", json);
        Assert.Contains("\"OMCL1:", json, StringComparison.Ordinal);
        // 非敏感字段保持明文
        Assert.Contains("Steve", json);
        Assert.Contains("069a79f4-44e9-4726-a5be-fca90e38aaf5", json);
        Assert.Contains("https://example.com/skin.png", json);

        // 反序列化还原为明文
        var back = JsonSerializer.Deserialize<List<GameAccount>>(json, TestOptions);
        Assert.NotNull(back);
        var restored = Assert.Single(back);
        Assert.Equal("microsoft-at", restored.AccessToken);
        Assert.Equal("microsoft-rt", restored.RefreshToken);
        Assert.Equal("minecraft-at", restored.MinecraftAccessToken);
        Assert.Equal("ygg-at", restored.YggdrasilAccessToken);
        Assert.Equal("ygg-ct", restored.YggdrasilClientToken);
        Assert.Equal("Steve", restored.Username);
        Assert.Equal("https://example.com/skin.png", restored.SkinUrl);
    }

    [Fact]
    public void GameAccountConverter_ReadsLegacyPlaintextFile()
    {
        // 旧版 accounts.json：令牌明文、无前缀 → 反序列化后直接可用
        const string legacyJson = """
            [
              {
                "Id": "legacy-id-1",
                "Username": "Notch",
                "Type": 1,
                "UUID": "069a79f444e94726a5befca90e38aaf5",
                "AccessToken": "legacy-at",
                "RefreshToken": "legacy-rt",
                "MinecraftAccessToken": "legacy-mt",
                "ExpiresAt": "2026-08-17T13:00:00+08:00"
              }
            ]
            """;

        var back = JsonSerializer.Deserialize<List<GameAccount>>(legacyJson, TestOptions);
        Assert.NotNull(back);
        var restored = Assert.Single(back);
        Assert.Equal("legacy-at", restored.AccessToken);
        Assert.Equal("legacy-rt", restored.RefreshToken);
        Assert.Equal("legacy-mt", restored.MinecraftAccessToken);
        Assert.Equal("Notch", restored.Username);
        Assert.Equal("legacy-id-1", restored.Id);
    }
}
