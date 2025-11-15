using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Yggdrasil 外置登录认证服务
    /// </summary>
    public class YggdrasilAuthService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// 进度更新回调
        /// </summary>
        public Action<string>? OnProgressUpdate { get; set; }

        /// <summary>
        /// 使用 Yggdrasil 服务器登录
        /// </summary>
        public async Task<GameAccount?> LoginAsync(YggdrasilServer server, string username, string password)
        {
            try
            {
                OnProgressUpdate?.Invoke("正在连接到认证服务器...");

                var apiUrl = server.GetFullApiUrl();
                var authenticateUrl = $"{apiUrl}/authserver/authenticate";

                OnProgressUpdate?.Invoke("正在验证账号信息...");

                // 构建认证请求
                var clientToken = Guid.NewGuid().ToString("N");
                var requestData = new
                {
                    agent = new
                    {
                        name = "Minecraft",
                        version = 1
                    },
                    username = username,
                    password = password,
                    clientToken = clientToken,
                    requestUser = true
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 发送认证请求
                var response = await _httpClient.PostAsync(authenticateUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // 解析错误信息
                    try
                    {
                        var errorDoc = JsonDocument.Parse(responseContent);
                        if (errorDoc.RootElement.TryGetProperty("errorMessage", out var errorMsg))
                        {
                            throw new Exception($"认证失败: {errorMsg.GetString()}");
                        }
                    }
                    catch (JsonException)
                    {
                        // 忽略JSON解析错误
                    }

                    throw new Exception($"认证失败 (HTTP {response.StatusCode})");
                }

                OnProgressUpdate?.Invoke("正在处理认证结果...");

                // 解析认证响应
                var authResponse = JsonDocument.Parse(responseContent);
                var root = authResponse.RootElement;

                // 提取访问令牌
                if (!root.TryGetProperty("accessToken", out var accessToken))
                {
                    throw new Exception("认证响应缺少访问令牌");
                }

                // 提取选中的角色信息
                if (!root.TryGetProperty("selectedProfile", out var selectedProfile))
                {
                    throw new Exception("认证响应缺少角色信息");
                }

                var profileId = selectedProfile.GetProperty("id").GetString();
                var profileName = selectedProfile.GetProperty("name").GetString();

                if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(profileName))
                {
                    throw new Exception("无效的角色信息");
                }

                OnProgressUpdate?.Invoke("登录成功！");

                // 创建游戏账号
                var account = new GameAccount
                {
                    Username = profileName,
                    Type = AccountType.Yggdrasil,
                    UUID = profileId,
                    YggdrasilServerId = server.Id,
                    YggdrasilAccessToken = accessToken.GetString(),
                    YggdrasilClientToken = clientToken,
                    CreatedAt = DateTime.Now,
                    LastUsed = DateTime.Now
                };

                // 更新服务器最后使用时间
                YggdrasilServerService.Instance.UpdateLastUsed(server.Id);

                return account;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"网络连接失败: {ex.Message}", ex);
            }
            catch (TaskCanceledException)
            {
                throw new Exception("请求超时，请检查网络连接");
            }
            catch (Exception ex)
            {
                throw new Exception($"登录失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 刷新访问令牌
        /// </summary>
        public async Task<bool> RefreshTokenAsync(GameAccount account)
        {
            try
            {
                if (account.Type != AccountType.Yggdrasil)
                {
                    return false;
                }

                var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId ?? "");
                if (server == null)
                {
                    return false;
                }

                var apiUrl = server.GetFullApiUrl();
                var refreshUrl = $"{apiUrl}/authserver/refresh";

                // 构建刷新请求
                var requestData = new
                {
                    accessToken = account.YggdrasilAccessToken,
                    clientToken = account.YggdrasilClientToken,
                    requestUser = true
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 发送刷新请求
                var response = await _httpClient.PostAsync(refreshUrl, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var refreshResponse = JsonDocument.Parse(responseContent);
                var root = refreshResponse.RootElement;

                // 更新访问令牌
                if (root.TryGetProperty("accessToken", out var newAccessToken))
                {
                    account.YggdrasilAccessToken = newAccessToken.GetString();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"刷新 Yggdrasil 令牌失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证访问令牌是否有效
        /// </summary>
        public async Task<bool> ValidateTokenAsync(GameAccount account)
        {
            try
            {
                if (account.Type != AccountType.Yggdrasil)
                {
                    return false;
                }

                var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId ?? "");
                if (server == null)
                {
                    return false;
                }

                var apiUrl = server.GetFullApiUrl();
                var validateUrl = $"{apiUrl}/authserver/validate";

                // 构建验证请求
                var requestData = new
                {
                    accessToken = account.YggdrasilAccessToken,
                    clientToken = account.YggdrasilClientToken
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 发送验证请求
                var response = await _httpClient.PostAsync(validateUrl, content);
                
                // 204 表示令牌有效
                return response.StatusCode == System.Net.HttpStatusCode.NoContent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"验证 Yggdrasil 令牌失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 登出账号（使令牌失效）
        /// </summary>
        public async Task<bool> InvalidateTokenAsync(GameAccount account)
        {
            try
            {
                if (account.Type != AccountType.Yggdrasil)
                {
                    return false;
                }

                var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId ?? "");
                if (server == null)
                {
                    return false;
                }

                var apiUrl = server.GetFullApiUrl();
                var invalidateUrl = $"{apiUrl}/authserver/invalidate";

                // 构建登出请求
                var requestData = new
                {
                    accessToken = account.YggdrasilAccessToken,
                    clientToken = account.YggdrasilClientToken
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 发送登出请求
                var response = await _httpClient.PostAsync(invalidateUrl, content);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"登出 Yggdrasil 账号失败: {ex.Message}");
                return false;
            }
        }
    }
}
