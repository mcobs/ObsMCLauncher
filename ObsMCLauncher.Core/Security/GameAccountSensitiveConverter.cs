using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// <see cref="GameAccount"/> 的自定义 JSON 转换器：
/// 序列化时对标记 <see cref="SensitiveAttribute"/> 的字段加密（Base64 密文落盘），
/// 反序列化时解密回明文；其余字段行为与默认序列化完全一致。
/// 无 "OMCL1:" 前缀的值视为旧版明文直接返回，保证向后兼容。
/// </summary>
public sealed class GameAccountSensitiveConverter : JsonConverter<GameAccount>
{
    /// <summary>内部序列化 options：不含本转换器，避免递归；缩进由外层共享 options 控制。</summary>
    private static readonly JsonSerializerOptions InnerOptions = new();

    /// <summary>需要持久化的属性（排除 [JsonIgnore] 的计算属性与只读属性）。</summary>
    private static readonly PropertyInfo[] PersistedProps = typeof(GameAccount)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0)
        .Where(p => p.CanWrite)
        .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
        .ToArray();

    /// <summary>标记 [Sensitive] 的属性（令牌字段）。</summary>
    private static readonly PropertyInfo[] SensitiveProps = PersistedProps
        .Where(p => p.GetCustomAttribute<SensitiveAttribute>() != null)
        .ToArray();

    public override GameAccount? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var account = JsonSerializer.Deserialize<GameAccount>(ref reader, InnerOptions);
        if (account == null) return null;

        foreach (var prop in SensitiveProps)
        {
            var value = prop.GetValue(account) as string;
            if (string.IsNullOrEmpty(value) || !value.StartsWith(TokenCryptoService.Prefix, StringComparison.Ordinal))
                continue;

            try
            {
                prop.SetValue(account, TokenCryptoService.Decrypt(value));
            }
            catch (Exception ex)
            {
                // 密钥不可用（换机器/换用户/数据损坏）时置空，避免启动崩溃；
                // 账号保留在列表中，界面按"令牌失效"提示重新登录。
                DebugLogger.Warn("Account", "Decrypt", $"令牌字段 {prop.Name} 解密失败，已置空: {ex.Message}");
                prop.SetValue(account, null);
            }
        }

        return account;
    }

    public override void Write(Utf8JsonWriter writer, GameAccount value, JsonSerializerOptions options)
    {
        // 克隆一份并替换敏感字段为密文，避免污染内存中的明文对象。
        var clone = new GameAccount();
        foreach (var prop in PersistedProps)
        {
            var raw = prop.GetValue(value);
            prop.SetValue(clone, prop.GetCustomAttribute<SensitiveAttribute>() != null
                ? TokenCryptoService.Encrypt(raw as string)
                : raw);
        }

        JsonSerializer.Serialize(writer, clone, InnerOptions);
    }
}
