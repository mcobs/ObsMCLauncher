using System.Text.RegularExpressions;

namespace ObsMCLauncher.Core.Services.Crash;

/// <summary>
/// 崩溃报告脱敏：抹掉会泄露隐私的字段，便于插件把内容发给外部服务（例如交给 AI 分析）。
///
/// 处理项：启动参数里的 <c>--username</c> / <c>--accessToken</c>、JSON 形式的 accessToken、
/// <c>Setting user:</c> 日志行、Windows/macOS/Linux 用户目录中的用户名、Bearer Token。
///
/// 纯文本规则，不依赖任何运行环境，可直接单测。
/// </summary>
public static class CrashReportSanitizer
{
    private const string UserPlaceholder = "<user>";
    private const string NamePlaceholder = "<username>";
    private const string TokenPlaceholder = "<token>";

    private static readonly Regex WindowsUserDir = new(@"([A-Za-z]:\\Users\\)[^\\/\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MacUserDir = new(@"(/Users/)[^/\s""']+", RegexOptions.Compiled);
    private static readonly Regex LinuxUserDir = new(@"(/home/)[^/\s""']+", RegexOptions.Compiled);
    private static readonly Regex LaunchUserName = new(@"(--username(?:=|\s+))[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LaunchToken = new(@"(--accessToken(?:=|\s+))[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JsonToken = new(@"(""accessToken""\s*:\s*"")[^""]*("")", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QueryToken = new(@"(\baccessToken=)[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SettingUser = new(@"(Setting user:\s*)\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BearerToken = new(@"(\bBearer\s+)[A-Za-z0-9._\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>对文本做脱敏；null/空返回原值（null 归一为空串）</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var s = text;
        s = WindowsUserDir.Replace(s, "$1" + UserPlaceholder);
        s = MacUserDir.Replace(s, "$1" + UserPlaceholder);
        s = LinuxUserDir.Replace(s, "$1" + UserPlaceholder);
        s = LaunchUserName.Replace(s, "$1" + NamePlaceholder);
        s = LaunchToken.Replace(s, "$1" + TokenPlaceholder);
        s = JsonToken.Replace(s, "$1" + TokenPlaceholder + "$2");
        s = QueryToken.Replace(s, "$1" + TokenPlaceholder);
        s = SettingUser.Replace(s, "$1" + NamePlaceholder);
        s = BearerToken.Replace(s, "$1" + TokenPlaceholder);
        return s;
    }
}
