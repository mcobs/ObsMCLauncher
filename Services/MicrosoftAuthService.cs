using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 微软账户认证服务
    /// </summary>
    public class MicrosoftAuthService
    {
        // Azure 注册的应用程序 Client ID（需向微软申请启动器授权）
        private const string ClientId = "83c332d7-9874-4ede-9ca5-37cbda08c232";
        private const string RedirectUri = "http://localhost:35565/callback";
        
        private static readonly HttpClient _httpClient = CreateHttpClient();
        private LocalHttpServer? _localServer;

        /// <summary>
        /// 创建配置好的 HttpClient
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                // 允许自动重定向
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                
                // 配置 SSL/TLS
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                
                // 开发环境下放宽证书验证（生产环境应该移除或更严格）
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                {
                    // 在调试模式下，可以放宽验证
                    #if DEBUG
                    if (sslPolicyErrors != System.Net.Security.SslPolicyErrors.None)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MicrosoftAuth] SSL 证书警告: {sslPolicyErrors}");
                        // 调试模式下允许通过
                        return true;
                    }
                    #endif
                    return sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            // 设置默认请求头
            client.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
            
            return client;
        }

        /// <summary>
        /// 进度更新回调
        /// </summary>
        public Action<string>? OnProgressUpdate { get; set; }

        /// <summary>
        /// 授权URL生成回调
        /// </summary>
        public Action<string>? OnAuthUrlGenerated { get; set; }

        /// <summary>
        /// 微软登录（完整OAuth2流程）
        /// </summary>
        public async Task<GameAccount?> LoginAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Console.WriteLine("========== 开始微软账户登录 ==========");
                OnProgressUpdate?.Invoke("正在启动本地服务器...");
                cancellationToken.ThrowIfCancellationRequested();
                
                // 1. 构建授权URL
                var authUrl = BuildAuthUrl();
                Console.WriteLine($"授权URL: {authUrl}");

                // 2. 启动本地HTTP服务器并打开浏览器
                _localServer = new LocalHttpServer(35565);
                
                // 通知UI显示授权URL
                OnAuthUrlGenerated?.Invoke(authUrl);
                OnProgressUpdate?.Invoke("等待浏览器授权...");
                
                // 打开浏览器
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
                    Console.WriteLine("⚠️ 无法自动打开浏览器，请手动访问授权URL");
                }

                // 3. 等待回调（支持取消）
                Console.WriteLine("等待用户授权...");
                var authCode = await _localServer.WaitForAuthCodeAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine($"✅ 收到授权码: {authCode.Substring(0, Math.Min(10, authCode.Length))}...");

                // 4. 使用授权码获取微软访问令牌
                OnProgressUpdate?.Invoke("正在获取微软访问令牌...");
                cancellationToken.ThrowIfCancellationRequested();
                var msTokens = await GetMicrosoftTokenAsync(authCode);
                Console.WriteLine("✅ 获取微软访问令牌成功");

                // 5. 使用微软令牌进行Xbox Live认证
                OnProgressUpdate?.Invoke("正在进行 Xbox Live 认证...");
                cancellationToken.ThrowIfCancellationRequested();
                var xblToken = await AuthenticateXboxLiveAsync(msTokens.AccessToken);
                Console.WriteLine("✅ Xbox Live 认证成功");

                // 6. 使用XBL令牌获取XSTS令牌
                OnProgressUpdate?.Invoke("正在获取 XSTS 令牌...");
                cancellationToken.ThrowIfCancellationRequested();
                var xstsData = await GetXSTSTokenAsync(xblToken);
                Console.WriteLine($"✅ XSTS 认证成功，UserHash: {xstsData.UserHash}");

                // 7. 使用XSTS令牌登录Minecraft
                OnProgressUpdate?.Invoke("正在登录 Minecraft Services...");
                cancellationToken.ThrowIfCancellationRequested();
                var mcData = await LoginMinecraftAsync(xstsData.UserHash, xstsData.Token);
                Console.WriteLine($"✅ Minecraft 登录成功，AccessToken: {mcData.AccessToken.Substring(0, 10)}...");

                // 8. 检查是否拥有Minecraft
                OnProgressUpdate?.Invoke("正在验证 Minecraft 授权...");
                cancellationToken.ThrowIfCancellationRequested();
                var hasMinecraft = await CheckMinecraftOwnershipAsync(mcData.AccessToken);
                if (!hasMinecraft)
                {
                    Console.WriteLine("❌ 此账号未购买 Minecraft");
                    OnProgressUpdate?.Invoke("未检测到 Minecraft 授权");
                    return null;
                }
                Console.WriteLine("✅ 检测到 Minecraft 授权");

                // 9. 获取玩家信息
                OnProgressUpdate?.Invoke("正在获取玩家信息...");
                cancellationToken.ThrowIfCancellationRequested();
                var profile = await GetMinecraftProfileAsync(mcData.AccessToken);
                Console.WriteLine($"✅ 获取玩家信息成功: {profile.Name} ({profile.Id})");

                // 10. 创建账号对象
                var account = new GameAccount
                {
                    Type = AccountType.Microsoft,
                    Username = profile.Name,
                    Email = "Microsoft Account", // 可以从Graph API获取邮箱
                    UUID = FormatUUID(profile.Id),
                    MinecraftUUID = FormatUUID(profile.Id),
                    AccessToken = msTokens.AccessToken,
                    RefreshToken = msTokens.RefreshToken,
                    ExpiresAt = DateTime.Now.AddSeconds(msTokens.ExpiresIn),
                    MinecraftAccessToken = mcData.AccessToken,
                    IsDefault = false
                };

                Console.WriteLine("========== 微软账户登录完成 ==========");
                return account;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("❌ 微软登录已取消");
                OnProgressUpdate?.Invoke("登录已取消");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 微软登录失败: {ex.Message}");
                Debug.WriteLine($"微软登录错误详情: {ex}");
                throw;
            }
            finally
            {
                _localServer?.Stop();
            }
        }

        /// <summary>
        /// 刷新访问令牌
        /// </summary>
        public async Task<bool> RefreshTokenAsync(GameAccount account)
        {
            try
            {
                if (string.IsNullOrEmpty(account.RefreshToken))
                {
                    return false;
                }

                Console.WriteLine($"正在刷新 {account.Username} 的访问令牌...");

                var msTokens = await RefreshMicrosoftTokenAsync(account.RefreshToken);
                Console.WriteLine("✅ 刷新微软访问令牌成功");

                var xblToken = await AuthenticateXboxLiveAsync(msTokens.AccessToken);
                var xstsData = await GetXSTSTokenAsync(xblToken);
                var mcData = await LoginMinecraftAsync(xstsData.UserHash, xstsData.Token);

                // 更新账号信息
                account.AccessToken = msTokens.AccessToken;
                account.RefreshToken = msTokens.RefreshToken;
                account.ExpiresAt = DateTime.Now.AddSeconds(msTokens.ExpiresIn);
                account.MinecraftAccessToken = mcData.AccessToken;

                Console.WriteLine("✅ 令牌刷新完成");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 刷新令牌失败: {ex.Message}");
                return false;
            }
        }

        #region OAuth2 流程方法

        /// <summary>
        /// 构建授权URL
        /// </summary>
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

        /// <summary>
        /// 使用授权码获取微软访问令牌
        /// </summary>
        private async Task<MicrosoftTokenResponse> GetMicrosoftTokenAsync(string code)
        {
            var data = new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", RedirectUri }
            };

            var content = new FormUrlEncodedContent(data);
            var response = await _httpClient.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                content);

            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"获取微软令牌失败: {json}");
            }

            return JsonSerializer.Deserialize<MicrosoftTokenResponse>(json)
                ?? throw new Exception("解析微软令牌响应失败");
        }

        /// <summary>
        /// 使用刷新令牌获取新的访问令牌
        /// </summary>
        private async Task<MicrosoftTokenResponse> RefreshMicrosoftTokenAsync(string refreshToken)
        {
            var data = new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "refresh_token", refreshToken },
                { "grant_type", "refresh_token" }
            };

            var content = new FormUrlEncodedContent(data);
            var response = await _httpClient.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                content);

            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"刷新微软令牌失败: {json}");
            }

            return JsonSerializer.Deserialize<MicrosoftTokenResponse>(json)
                ?? throw new Exception("解析微软令牌响应失败");
        }

        /// <summary>
        /// Xbox Live 认证
        /// </summary>
        private async Task<string> AuthenticateXboxLiveAsync(string accessToken)
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
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "https://user.auth.xboxlive.com/user/authenticate",
                content);

            var responseJson = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Xbox Live 认证失败: {responseJson}");
            }

            var xblResponse = JsonSerializer.Deserialize<XboxLiveResponse>(responseJson);
            return xblResponse?.Token ?? throw new Exception("Xbox Live Token 为空");
        }

        /// <summary>
        /// 获取 XSTS 令牌
        /// </summary>
        private async Task<(string Token, string UserHash)> GetXSTSTokenAsync(string xblToken)
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
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "https://xsts.auth.xboxlive.com/xsts/authorize",
                content);

            var responseJson = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"XSTS 认证失败: {responseJson}");
            }

            var xstsResponse = JsonSerializer.Deserialize<XSTSResponse>(responseJson);
            if (xstsResponse == null || xstsResponse.DisplayClaims?.xui == null || xstsResponse.DisplayClaims.xui.Length == 0)
            {
                throw new Exception("XSTS 响应无效");
            }

            return (xstsResponse.Token, xstsResponse.DisplayClaims.xui[0].uhs);
        }

        /// <summary>
        /// Minecraft 登录
        /// </summary>
        private async Task<MinecraftLoginResponse> LoginMinecraftAsync(string userHash, string xstsToken)
        {
            const int maxRetries = 3;
            Exception? lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Debug.WriteLine($"[MicrosoftAuth] 尝试 Minecraft 登录 ({i + 1}/{maxRetries})");

                    var requestData = new
                    {
                        identityToken = $"XBL3.0 x={userHash};{xstsToken}"
                    };

                    var json = JsonSerializer.Serialize(requestData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync(
                        "https://api.minecraftservices.com/authentication/login_with_xbox",
                        content);

                    var responseJson = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Minecraft 登录失败: {responseJson}");
                    }

                    return JsonSerializer.Deserialize<MinecraftLoginResponse>(responseJson)
                        ?? throw new Exception("解析 Minecraft 登录响应失败");
                }
                catch (HttpRequestException ex) when (i < maxRetries - 1)
                {
                    Debug.WriteLine($"[MicrosoftAuth] Minecraft 登录失败 (尝试 {i + 1}/{maxRetries}): {ex.Message}");
                    lastException = ex;
                    
                    // 等待一段时间后重试
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }

            throw lastException ?? new Exception("Minecraft 登录失败");
        }

        /// <summary>
        /// 检查 Minecraft 所有权
        /// </summary>
        private async Task<bool> CheckMinecraftOwnershipAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var ownership = JsonSerializer.Deserialize<MinecraftOwnershipResponse>(json);
            return ownership?.items?.Length > 0;
        }

        /// <summary>
        /// 获取 Minecraft 玩家信息
        /// </summary>
        private async Task<MinecraftProfile> GetMinecraftProfileAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"获取玩家信息失败: {json}");
            }

            return JsonSerializer.Deserialize<MinecraftProfile>(json)
                ?? throw new Exception("解析玩家信息失败");
        }

        /// <summary>
        /// 格式化 UUID（添加连字符）
        /// </summary>
        private string FormatUUID(string uuid)
        {
            if (uuid.Length != 32) return uuid;
            return $"{uuid.Substring(0, 8)}-{uuid.Substring(8, 4)}-{uuid.Substring(12, 4)}-{uuid.Substring(16, 4)}-{uuid.Substring(20)}";
        }

        #endregion

        #region 响应模型

        private class MicrosoftTokenResponse
        {
            public string access_token { get; set; } = "";
            public string refresh_token { get; set; } = "";
            public int expires_in { get; set; }

            public string AccessToken => access_token;
            public string RefreshToken => refresh_token;
            public int ExpiresIn => expires_in;
        }

        private class XboxLiveResponse
        {
            public string Token { get; set; } = "";
        }

        private class XSTSResponse
        {
            public string Token { get; set; } = "";
            public DisplayClaims? DisplayClaims { get; set; }
        }

        private class DisplayClaims
        {
            public XuiClaim[]? xui { get; set; }
        }

        private class XuiClaim
        {
            public string uhs { get; set; } = "";
        }

        private class MinecraftLoginResponse
        {
            public string access_token { get; set; } = "";
            public string AccessToken => access_token;
        }

        private class MinecraftOwnershipResponse
        {
            public Item[]? items { get; set; }

            public class Item
            {
                public string name { get; set; } = "";
            }
        }

        private class MinecraftProfile
        {
            public string id { get; set; } = "";
            public string name { get; set; } = "";

            public string Id => id;
            public string Name => name;
        }

        #endregion
    }
}