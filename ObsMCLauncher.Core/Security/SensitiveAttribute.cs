using System;

namespace ObsMCLauncher.Core.Security;

/// <summary>
/// 标记需要在持久化时加密的敏感属性（如账号令牌）。
/// 由 <see cref="GameAccountSensitiveConverter"/> 在序列化边界统一处理，
/// 内存中保持明文以便业务层正常使用。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute : Attribute
{
}
