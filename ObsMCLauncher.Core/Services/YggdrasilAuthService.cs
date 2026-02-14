using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

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

public class YggdrasilAuthService
{
    private const string ServiceName = "YggdrasilAuth";
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public Action<string>? OnProgressUpdate { get; set; }

    public async Task<GameAccount?> LoginAsync(YggdrasilServer server, string username, string password)
    {
        try
        {
            OnProgressUpdate?.Invoke("正在连接到认证服务器...");

            var apiUrl = server.GetFullApiUrl();
            DebugLogger.Info(ServiceName, "Login", $"API URL: {apiUrl}");
            
            var authenticateUrl = $"{apiUrl}/authserver/authenticate";
            DebugLogger.Info(ServiceName, "Login", $"Authenticate URL: {authenticateUrl}");

            OnProgressUpdate?.Invoke("正在验证账号信息...");

            var clientToken = Guid.NewGuid().ToString("N");
            var requestData = new
            {
                agent = new
                {
                    name = "Minecraft",
                    version = 1
                },
                username,
                password,
                clientToken,
                requestUser = true
            };

            var jsonContent = JsonSerializer.Serialize(requestData);
            DebugLogger.Info(ServiceName, "Login", $"Request prepared for user: {username}");
            
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(authenticateUrl, content).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                DebugLogger.Error(ServiceName, "Login", $"HTTP请求失败: {ex.Message}");
                throw new Exception($"无法连接到认证服务器: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                DebugLogger.Warn(ServiceName, "Login", "连接超时");
                throw new Exception("连接认证服务器超时，请检查网络或服务器地址");
            }

            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            DebugLogger.Info(ServiceName, "Login", $"Response Status: {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var errorDoc = JsonDocument.Parse(responseContent);
                    if (errorDoc.RootElement.TryGetProperty("errorMessage", out var errorMsg))
                    {
                        var message = errorMsg.GetString() ?? "未知错误";
                        DebugLogger.Warn(ServiceName, "Login", $"认证失败: {message}");
                        throw new Exception($"认证失败: {message}");
                    }
                    if (errorDoc.RootElement.TryGetProperty("error", out var error))
                    {
                        var errorStr = error.GetString() ?? "未知错误";
                        var cause = errorDoc.RootElement.TryGetProperty("cause", out var causeEl) ? causeEl.GetString() : "";
                        DebugLogger.Warn(ServiceName, "Login", $"认证错误: {errorStr} - {cause}");
                        throw new Exception($"认证失败: {errorStr}{(string.IsNullOrEmpty(cause) ? "" : $" - {cause}")}");
                    }
                }
                catch (JsonException)
                {
                }

                throw new Exception($"认证失败 (HTTP {(int)response.StatusCode})");
            }

            OnProgressUpdate?.Invoke("正在处理认证结果...");

            var authResponse = JsonDocument.Parse(responseContent);
            var root = authResponse.RootElement;

            if (!root.TryGetProperty("accessToken", out var accessToken))
            {
                DebugLogger.Error(ServiceName, "Login", "认证响应缺少访问令牌");
                throw new Exception("认证响应缺少访问令牌");
            }

            JsonElement profileElement;
            if (root.TryGetProperty("selectedProfile", out var selectedProfile))
            {
                profileElement = selectedProfile;
            }
            else if (root.TryGetProperty("availableProfiles", out var availableProfiles) &&
                     availableProfiles.ValueKind == JsonValueKind.Array &&
                     availableProfiles.GetArrayLength() > 0)
            {
                var profiles = new List<(string id, string name)>();
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
                    DebugLogger.Info(ServiceName, "Login", $"发现多个角色: {profiles.Count}个");
                    throw new MultipleProfilesException(profiles, accessToken.GetString()!, clientToken);
                }

                if (profiles.Count == 1)
                {
                    profileElement = availableProfiles[0];
                }
                else
                {
                    DebugLogger.Warn(ServiceName, "Login", "没有可用的角色");
                    throw new Exception("认证响应中没有可用的角色");
                }
            }
            else
            {
                DebugLogger.Error(ServiceName, "Login", "认证响应缺少角色信息");
                throw new Exception("认证响应缺少角色信息");
            }

            var profileId = profileElement.GetProperty("id").GetString();
            var profileName = profileElement.GetProperty("name").GetString();

            if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(profileName))
            {
                DebugLogger.Error(ServiceName, "Login", "无效的角色信息");
                throw new Exception("无效的角色信息");
            }

            OnProgressUpdate?.Invoke("登录成功！");
            DebugLogger.Info(ServiceName, "Login", $"登录成功: {profileName}");

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

            YggdrasilServerService.Instance.UpdateLastUsed(server.Id);

            return account;
        }
        catch (HttpRequestException ex)
        {
            DebugLogger.Error(ServiceName, "Login", $"网络错误: {ex.Message}");
            throw new Exception($"网络连接失败: {ex.Message}", ex);
        }
        catch (TaskCanceledException)
        {
            DebugLogger.Warn(ServiceName, "Login", "请求超时");
            throw new Exception("请求超时，请检查网络连接");
        }
        catch (MultipleProfilesException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLogger.Error(ServiceName, "Login", $"登录失败: {ex.Message}");
            throw new Exception($"登录失败: {ex.Message}", ex);
        }
    }

    public GameAccount CreateAccountFromProfile(YggdrasilServer server, string profileId, string profileName,
        string accessToken, string clientToken)
    {
        DebugLogger.Info(ServiceName, "CreateAccount", $"创建账号: {profileName}");
        
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

        YggdrasilServerService.Instance.UpdateLastUsed(server.Id);

        return account;
    }

    public async Task<bool> RefreshTokenAsync(GameAccount account)
    {
        try
        {
            if (account.Type != AccountType.Yggdrasil) return false;

            var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId ?? "");
            if (server == null)
            {
                DebugLogger.Warn(ServiceName, "RefreshToken", $"未找到服务器: {account.YggdrasilServerId}");
                return false;
            }

            var apiUrl = server.GetFullApiUrl();
            var refreshUrl = $"{apiUrl}/authserver/refresh";
            
            DebugLogger.Info(ServiceName, "RefreshToken", $"刷新令牌: {account.Username}");

            var requestData = new
            {
                accessToken = account.YggdrasilAccessToken,
                clientToken = account.YggdrasilClientToken,
                requestUser = true
            };

            var jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(refreshUrl, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                DebugLogger.Warn(ServiceName, "RefreshToken", $"刷新失败: {(int)response.StatusCode}");
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var refreshResponse = JsonDocument.Parse(responseContent);
            var root = refreshResponse.RootElement;

            if (root.TryGetProperty("accessToken", out var newAccessToken))
            {
                account.YggdrasilAccessToken = newAccessToken.GetString();
                DebugLogger.Info(ServiceName, "RefreshToken", "令牌刷新成功");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugLogger.Error(ServiceName, "RefreshToken", $"刷新失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ValidateTokenAsync(GameAccount account)
    {
        try
        {
            if (account.Type != AccountType.Yggdrasil) return false;

            var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId ?? "");
            if (server == null) return false;

            var apiUrl = server.GetFullApiUrl();
            var validateUrl = $"{apiUrl}/authserver/validate";
            
            DebugLogger.Info(ServiceName, "ValidateToken", $"验证令牌: {account.Username}");

            var requestData = new
            {
                accessToken = account.YggdrasilAccessToken,
                clientToken = account.YggdrasilClientToken
            };

            var jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(validateUrl, content).ConfigureAwait(false);
            var isValid = response.StatusCode == System.Net.HttpStatusCode.NoContent;
            DebugLogger.Info(ServiceName, "ValidateToken", $"验证结果: {isValid}");
            return isValid;
        }
        catch (Exception ex)
        {
            DebugLogger.Error(ServiceName, "ValidateToken", $"验证失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> InvalidateTokenAsync(GameAccount account)
    {
        try
        {
            if (account.Type != AccountType.Yggdrasil) return false;

            var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId ?? "");
            if (server == null) return false;

            var apiUrl = server.GetFullApiUrl();
            var invalidateUrl = $"{apiUrl}/authserver/invalidate";
            
            DebugLogger.Info(ServiceName, "InvalidateToken", $"登出账号: {account.Username}");

            var requestData = new
            {
                accessToken = account.YggdrasilAccessToken,
                clientToken = account.YggdrasilClientToken
            };

            var jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(invalidateUrl, content).ConfigureAwait(false);
            var success = response.IsSuccessStatusCode;
            DebugLogger.Info(ServiceName, "InvalidateToken", $"登出结果: {success}");
            return success;
        }
        catch (Exception ex)
        {
            DebugLogger.Error(ServiceName, "InvalidateToken", $"登出失败: {ex.Message}");
            return false;
        }
    }
}
