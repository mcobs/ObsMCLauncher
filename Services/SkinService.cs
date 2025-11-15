using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 皮肤服务 - 负责获取和缓存 Minecraft 皮肤
    /// </summary>
    public class SkinService
    {
        private static readonly Lazy<SkinService> _instance = new(() => new SkinService());
        public static SkinService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private readonly string _skinCacheDir;

        private SkinService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // 皮肤缓存目录
            var config = LauncherConfig.Load();
            _skinCacheDir = Path.Combine(config.GameDirectory, "launcher_cache", "skins");
            Directory.CreateDirectory(_skinCacheDir);
        }

        /// <summary>
        /// 获取账号的皮肤头像路径（如果不存在则下载）
        /// </summary>
        public async Task<string?> GetSkinHeadPathAsync(GameAccount account, bool forceRefresh = false)
        {
            try
            {
                // 检查是否需要刷新皮肤
                bool needRefresh = forceRefresh ||
                                   string.IsNullOrEmpty(account.CachedSkinPath) ||
                                   !File.Exists(account.CachedSkinPath) ||
                                   account.SkinLastUpdated == null ||
                                   DateTime.Now - account.SkinLastUpdated.Value > TimeSpan.FromDays(7);

                if (!needRefresh && !string.IsNullOrEmpty(account.CachedSkinPath))
                {
                    return account.CachedSkinPath;
                }

                // 根据账号类型获取皮肤
                string? skinUrl = null;

                switch (account.Type)
                {
                    case AccountType.Microsoft:
                        skinUrl = await GetMicrosoftSkinUrlAsync(account);
                        break;

                    case AccountType.Yggdrasil:
                        skinUrl = await GetYggdrasilSkinUrlAsync(account);
                        break;

                    case AccountType.Offline:
                        // 离线账户使用默认皮肤
                        skinUrl = GetDefaultSkinUrl(account.Username);
                        break;
                }

                if (string.IsNullOrEmpty(skinUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"[SkinService] 无法获取皮肤URL: {account.Username}");
                    return null;
                }

                // 下载皮肤
                var skinPath = await DownloadSkinAsync(account.Id, skinUrl);
                if (string.IsNullOrEmpty(skinPath))
                {
                    return null;
                }

                // 更新账号信息
                account.SkinUrl = skinUrl;
                account.CachedSkinPath = skinPath;
                account.SkinLastUpdated = DateTime.Now;

                // 保存账号信息
                AccountService.Instance.SaveAccountsData();

                return skinPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinService] 获取皮肤失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取微软账户的皮肤 URL
        /// </summary>
        private async Task<string?> GetMicrosoftSkinUrlAsync(GameAccount account)
        {
            try
            {
                if (string.IsNullOrEmpty(account.MinecraftUUID))
                {
                    return null;
                }

                // 移除 UUID 中的连字符
                var uuid = account.MinecraftUUID.Replace("-", "");

                // 调用 Mojang API 获取皮肤信息
                var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
                var response = await _httpClient.GetStringAsync(url);

                var profileData = JsonSerializer.Deserialize<JsonElement>(response);

                if (profileData.TryGetProperty("properties", out var properties))
                {
                    foreach (var property in properties.EnumerateArray())
                    {
                        if (property.TryGetProperty("name", out var name) && name.GetString() == "textures")
                        {
                            if (property.TryGetProperty("value", out var value))
                            {
                                // Base64 解码
                                var texturesJson = System.Text.Encoding.UTF8.GetString(
                                    Convert.FromBase64String(value.GetString() ?? "")
                                );

                                var texturesData = JsonSerializer.Deserialize<JsonElement>(texturesJson);

                                if (texturesData.TryGetProperty("textures", out var textures) &&
                                    textures.TryGetProperty("SKIN", out var skin) &&
                                    skin.TryGetProperty("url", out var skinUrl))
                                {
                                    return skinUrl.GetString();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinService] 获取微软皮肤失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 获取 Yggdrasil 外置登录账户的皮肤 URL
        /// </summary>
        private async Task<string?> GetYggdrasilSkinUrlAsync(GameAccount account)
        {
            try
            {
                if (string.IsNullOrEmpty(account.YggdrasilServerId) || string.IsNullOrEmpty(account.UUID))
                {
                    System.Diagnostics.Debug.WriteLine($"[SkinService] Yggdrasil账号缺少ServerId或UUID");
                    return null;
                }

                // 获取 Yggdrasil 服务器配置
                var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId);
                if (server == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SkinService] 找不到Yggdrasil服务器: {account.YggdrasilServerId}");
                    return null;
                }

                // 移除 UUID 中的连字符
                var uuid = account.UUID.Replace("-", "");

                // 使用 GetFullApiUrl() 获取完整 API 地址
                var baseUrl = server.GetFullApiUrl();
                var url = $"{baseUrl.TrimEnd('/')}/sessionserver/session/minecraft/profile/{uuid}";
                
                System.Diagnostics.Debug.WriteLine($"[SkinService] 请求Yggdrasil皮肤: {url}");

                var response = await _httpClient.GetStringAsync(url);
                System.Diagnostics.Debug.WriteLine($"[SkinService] Yggdrasil响应: {response.Substring(0, Math.Min(200, response.Length))}...");

                var profileData = JsonSerializer.Deserialize<JsonElement>(response);

                if (profileData.TryGetProperty("properties", out var properties))
                {
                    foreach (var property in properties.EnumerateArray())
                    {
                        if (property.TryGetProperty("name", out var name) && name.GetString() == "textures")
                        {
                            if (property.TryGetProperty("value", out var value))
                            {
                                // Base64 解码
                                var texturesJson = System.Text.Encoding.UTF8.GetString(
                                    Convert.FromBase64String(value.GetString() ?? "")
                                );

                                System.Diagnostics.Debug.WriteLine($"[SkinService] Textures数据: {texturesJson}");

                                var texturesData = JsonSerializer.Deserialize<JsonElement>(texturesJson);

                                if (texturesData.TryGetProperty("textures", out var textures) &&
                                    textures.TryGetProperty("SKIN", out var skin) &&
                                    skin.TryGetProperty("url", out var skinUrl))
                                {
                                    var skinUrlString = skinUrl.GetString();
                                    System.Diagnostics.Debug.WriteLine($"[SkinService] 获取到Yggdrasil皮肤URL: {skinUrlString}");
                                    return skinUrlString;
                                }
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[SkinService] Yggdrasil响应中未找到皮肤URL");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinService] 获取Yggdrasil皮肤失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[SkinService] 堆栈跟踪: {ex.StackTrace}");
            }

            return null;
        }

        /// <summary>
        /// 获取默认皮肤 URL（离线账户）
        /// </summary>
        private string GetDefaultSkinUrl(string username)
        {
            // 使用 Minecraft 默认皮肤（Steve 或 Alex）
            // 根据用户名哈希决定使用哪个皮肤
            var hash = Math.Abs(username.GetHashCode());
            var isAlex = hash % 2 == 0;

            // 使用 Crafatar 服务获取默认皮肤
            return isAlex
                ? "https://crafatar.com/skins/MHF_Alex"
                : "https://crafatar.com/skins/MHF_Steve";
        }

        /// <summary>
        /// 下载皮肤文件
        /// </summary>
        private async Task<string?> DownloadSkinAsync(string accountId, string skinUrl)
        {
            try
            {
                var skinPath = Path.Combine(_skinCacheDir, $"{accountId}.png");

                var response = await _httpClient.GetAsync(skinUrl);
                response.EnsureSuccessStatusCode();

                var skinData = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(skinPath, skinData);

                System.Diagnostics.Debug.WriteLine($"[SkinService] 皮肤已下载: {skinPath}");
                return skinPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinService] 下载皮肤失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 清除账号的皮肤缓存
        /// </summary>
        public void ClearSkinCache(string accountId)
        {
            try
            {
                var skinPath = Path.Combine(_skinCacheDir, $"{accountId}.png");
                if (File.Exists(skinPath))
                {
                    File.Delete(skinPath);
                    System.Diagnostics.Debug.WriteLine($"[SkinService] 已清除皮肤缓存: {accountId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinService] 清除皮肤缓存失败: {ex.Message}");
            }
        }
    }
}
