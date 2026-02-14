using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Accounts;

public sealed class MicrosoftAuthService
{
    private const string ClientId = "83c332d7-9874-4ede-9ca5-37cbda08c232";
    private const string RedirectUri = "http://localhost:35565/callback";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private LocalHttpServer? _localServer;

    public Action<string>? OnProgressUpdate { get; set; }

    public Action<string>? OnAuthUrlGenerated { get; set; }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
#if DEBUG
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
#endif
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        return client;
    }

    public async Task<GameAccount?> LoginAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            OnProgressUpdate?.Invoke("正在启动本地服务器...");
            cancellationToken.ThrowIfCancellationRequested();

            var authUrl = BuildAuthUrl();

            _localServer = new LocalHttpServer(35565);

            OnAuthUrlGenerated?.Invoke(authUrl);
            OnProgressUpdate?.Invoke("等待浏览器授权...");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
            }

            var authCode = await _localServer.WaitForAuthCodeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            OnProgressUpdate?.Invoke("正在获取微软访问令牌...");
            var msTokens = await GetMicrosoftTokenAsync(authCode, cancellationToken);

            OnProgressUpdate?.Invoke("正在进行 Xbox Live 认证...");
            var xblToken = await AuthenticateXboxLiveAsync(msTokens.AccessToken, cancellationToken);

            OnProgressUpdate?.Invoke("正在获取 XSTS 令牌...");
            var (xstsToken, userHash) = await GetXstsTokenAsync(xblToken, cancellationToken);

            OnProgressUpdate?.Invoke("正在登录 Minecraft Services...");
            var mcToken = await LoginMinecraftAsync(userHash, xstsToken, cancellationToken);

            OnProgressUpdate?.Invoke("正在验证 Minecraft 授权...");
            var hasMc = await CheckMinecraftOwnershipAsync(mcToken, cancellationToken);
            if (!hasMc)
            {
                OnProgressUpdate?.Invoke("未检测到 Minecraft 授权");
                return null;
            }

            OnProgressUpdate?.Invoke("正在获取玩家信息...");
            var profile = await GetMinecraftProfileAsync(mcToken, cancellationToken);

            var account = new GameAccount
            {
                Type = AccountType.Microsoft,
                Username = profile.Name,
                Email = "Microsoft Account",
                UUID = FormatUuid(profile.Id),
                MinecraftUUID = FormatUuid(profile.Id),
                AccessToken = msTokens.AccessToken,
                RefreshToken = msTokens.RefreshToken,
                ExpiresAt = DateTime.Now.AddSeconds(msTokens.ExpiresIn),
                MinecraftAccessToken = mcToken,
                IsDefault = false
            };

            OnProgressUpdate?.Invoke("微软登录完成");
            return account;
        }
        finally
        {
            _localServer?.Stop();
        }
    }

    public async Task<bool> RefreshTokenAsync(GameAccount account, CancellationToken cancellationToken = default)
    {
        if (account.Type != AccountType.Microsoft)
            return false;

        if (string.IsNullOrWhiteSpace(account.RefreshToken))
            return false;

        try
        {
            OnProgressUpdate?.Invoke("正在刷新微软访问令牌...");
            var msTokens = await RefreshMicrosoftTokenAsync(account.RefreshToken, cancellationToken);

            OnProgressUpdate?.Invoke("正在进行 Xbox Live 认证...");
            var xblToken = await AuthenticateXboxLiveAsync(msTokens.AccessToken, cancellationToken);

            OnProgressUpdate?.Invoke("正在获取 XSTS 令牌...");
            var (xstsToken, userHash) = await GetXstsTokenAsync(xblToken, cancellationToken);

            OnProgressUpdate?.Invoke("正在登录 Minecraft Services...");
            var mcToken = await LoginMinecraftAsync(userHash, xstsToken, cancellationToken);

            // 更新账号信息
            account.AccessToken = msTokens.AccessToken;
            account.RefreshToken = msTokens.RefreshToken;
            account.ExpiresAt = DateTime.Now.AddSeconds(msTokens.ExpiresIn);
            account.MinecraftAccessToken = mcToken;

            OnProgressUpdate?.Invoke("令牌刷新完成");
            return true;
        }
        catch (OperationCanceledException)
        {
            OnProgressUpdate?.Invoke("令牌刷新已取消");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MicrosoftAuth] RefreshToken failed: {ex}");
            OnProgressUpdate?.Invoke($"令牌刷新失败: {ex.Message}");
            return false;
        }
    }

    private string BuildAuthUrl()
    {
        var scopes = Uri.EscapeDataString("XboxLive.signin offline_access");
        return $"https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?" +
               $"client_id={ClientId}" +
               $"&response_type=code" +
               $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
               $"&scope={scopes}" +
               $"&response_mode=query";
    }

    private async Task<MicrosoftTokenResponse> GetMicrosoftTokenAsync(string code, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string>
        {
            { "client_id", ClientId },
            { "code", code },
            { "grant_type", "authorization_code" },
            { "redirect_uri", RedirectUri }
        };

        using var content = new FormUrlEncodedContent(data);
        using var response = await HttpClient.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
            content,
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"获取微软令牌失败: {json}");

        return JsonSerializer.Deserialize<MicrosoftTokenResponse>(json)
               ?? throw new Exception("解析微软令牌响应失败");
    }

    private async Task<MicrosoftTokenResponse> RefreshMicrosoftTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string>
        {
            { "client_id", ClientId },
            { "refresh_token", refreshToken },
            { "grant_type", "refresh_token" }
        };

        using var content = new FormUrlEncodedContent(data);
        using var response = await HttpClient.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
            content,
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"刷新微软令牌失败: {json}");

        return JsonSerializer.Deserialize<MicrosoftTokenResponse>(json)
               ?? throw new Exception("解析微软令牌响应失败");
    }

    private async Task<string> AuthenticateXboxLiveAsync(string accessToken, CancellationToken cancellationToken)
    {
        var requestData = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={accessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        var json = JsonSerializer.Serialize(requestData);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await HttpClient.PostAsync(
            "https://user.auth.xboxlive.com/user/authenticate",
            content,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Xbox Live 认证失败: {responseJson}");

        var xbl = JsonSerializer.Deserialize<XboxLiveResponse>(responseJson);
        return xbl?.Token ?? throw new Exception("Xbox Live Token 为空");
    }

    private async Task<(string Token, string UserHash)> GetXstsTokenAsync(string xblToken, CancellationToken cancellationToken)
    {
        var requestData = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        var json = JsonSerializer.Serialize(requestData);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await HttpClient.PostAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            content,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"XSTS 认证失败: {responseJson}");

        var xsts = JsonSerializer.Deserialize<XstsResponse>(responseJson);
        if (xsts?.DisplayClaims?.xui == null || xsts.DisplayClaims.xui.Length == 0)
            throw new Exception("XSTS 响应无效");

        return (xsts.Token, xsts.DisplayClaims.xui[0].uhs);
    }

    private async Task<string> LoginMinecraftAsync(string userHash, string xstsToken, CancellationToken cancellationToken)
    {
        var requestData = new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        };

        var json = JsonSerializer.Serialize(requestData);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await HttpClient.PostAsync(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            content,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Minecraft 登录失败: {responseJson}");

        var mc = JsonSerializer.Deserialize<MinecraftLoginResponse>(responseJson);
        return mc?.AccessToken ?? throw new Exception("Minecraft AccessToken 为空");
    }

    private async Task<bool> CheckMinecraftOwnershipAsync(string mcAccessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore");
        request.Headers.Add("Authorization", $"Bearer {mcAccessToken}");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            return false;

        var ownership = JsonSerializer.Deserialize<MinecraftOwnershipResponse>(json);
        return ownership?.items != null && ownership.items.Length > 0;
    }

    private async Task<MinecraftProfile> GetMinecraftProfileAsync(string mcAccessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Add("Authorization", $"Bearer {mcAccessToken}");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"获取玩家信息失败: {json}");

        return JsonSerializer.Deserialize<MinecraftProfile>(json)
               ?? throw new Exception("解析玩家信息失败");
    }

    private static string FormatUuid(string uuid)
    {
        if (uuid.Length != 32) return uuid;
        return $"{uuid.Substring(0, 8)}-{uuid.Substring(8, 4)}-{uuid.Substring(12, 4)}-{uuid.Substring(16, 4)}-{uuid.Substring(20)}";
    }

    private sealed class MicrosoftTokenResponse
    {
        public string access_token { get; set; } = "";
        public string refresh_token { get; set; } = "";
        public int expires_in { get; set; }

        public string AccessToken => access_token;
        public string RefreshToken => refresh_token;
        public int ExpiresIn => expires_in;
    }

    private sealed class XboxLiveResponse
    {
        public string Token { get; set; } = "";
    }

    private sealed class XstsResponse
    {
        public string Token { get; set; } = "";
        public DisplayClaims? DisplayClaims { get; set; }
    }

    private sealed class DisplayClaims
    {
        public XuiClaim[]? xui { get; set; }
    }

    private sealed class XuiClaim
    {
        public string uhs { get; set; } = "";
    }

    private sealed class MinecraftLoginResponse
    {
        public string access_token { get; set; } = "";
        public string AccessToken => access_token;
    }

    private sealed class MinecraftOwnershipResponse
    {
        public Item[]? items { get; set; }

        public sealed class Item
        {
            public string name { get; set; } = "";
        }
    }

    private sealed class MinecraftProfile
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";

        public string Id => id;
        public string Name => name;
    }
}
