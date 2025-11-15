using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 多角色异常，用于处理用户有多个角色的情况
    /// </summary>
    public class MultipleProfilesException : Exception
    {
        public List<(string id, string name)> Profiles { get; }
        public string AccessToken { get; }
        public string ClientToken { get; }

        public MultipleProfilesException(List<(string id, string name)> profiles, string accessToken, string clientToken)
            : base("用户有多个角色，需要选择")
        {
            Profiles = profiles;
            AccessToken = accessToken;
            ClientToken = clientToken;
        }
    }

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

                // 提取角色信息
                // 优先使用 selectedProfile，如果不存在则检查 availableProfiles
                JsonElement profileElement;
                if (root.TryGetProperty("selectedProfile", out var selectedProfile))
                {
                    profileElement = selectedProfile;
                }
                else if (root.TryGetProperty("availableProfiles", out var availableProfiles) && 
                         availableProfiles.ValueKind == JsonValueKind.Array && 
                         availableProfiles.GetArrayLength() > 0)
                {
                    // 如果有多个角色，返回特殊结果，让调用者处理角色选择
                    var profiles = new System.Collections.Generic.List<(string id, string name)>();
                    foreach (var profile in availableProfiles.EnumerateArray())
                    {
                        var id = profile.GetProperty("id").GetString();
                        var name = profile.GetProperty("name").GetString();
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        {
                            profiles.Add((id, name));
                        }
                    }
                    
                    if (profiles.Count > 1)
                    {
                        // 多个角色，需要用户选择
                        throw new MultipleProfilesException(profiles, accessToken.GetString()!, clientToken);
                    }
                    else if (profiles.Count == 1)
                    {
                        // 只有一个角色，直接使用
                        profileElement = availableProfiles[0];
                    }
                    else
                    {
                        throw new Exception("认证响应中没有可用的角色");
                    }
                }
                else
                {
                    throw new Exception("认证响应缺少角色信息");
                }

                var profileId = profileElement.GetProperty("id").GetString();
                var profileName = profileElement.GetProperty("name").GetString();

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
            catch (MultipleProfilesException)
            {
                // 多角色异常直接抛出，让调用者处理
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"登录失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 根据选择的角色创建账号（用于多角色情况）
        /// </summary>
        public GameAccount CreateAccountFromProfile(YggdrasilServer server, string profileId, string profileName, 
            string accessToken, string clientToken)
        {
            var account = new GameAccount
            {
                Username = profileName,
                Type = AccountType.Yggdrasil,
                UUID = profileId,
                YggdrasilServerId = server.Id,
                YggdrasilAccessToken = accessToken,
                YggdrasilClientToken = clientToken,
                CreatedAt = DateTime.Now,
                LastUsed = DateTime.Now
            };

            // 更新服务器最后使用时间
            YggdrasilServerService.Instance.UpdateLastUsed(server.Id);

            return account;
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
