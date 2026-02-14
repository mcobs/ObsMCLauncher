using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services
{
    public class SkinService
    {
        private static readonly Lazy<SkinService> _instance = new(() => new SkinService());
        public static SkinService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private readonly string _skinCacheDir;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

        private SkinService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            var config = LauncherConfig.Load();
            _skinCacheDir = Path.Combine(config.GetDataDirectory(), "cache", "skins");
            Directory.CreateDirectory(_skinCacheDir);
        }

        /// <summary>
        /// 获取账号的皮肤全图路径（如果不存在则下载）
        /// </summary>
        public async Task<string?> GetSkinPathAsync(GameAccount account, bool forceRefresh = false)
        {
            // 为每个账号获取或创建一个锁，防止并发获取同一账号的皮肤
            var accountLockKey = $"get_{account.Id}";
            var accountSemaphore = _downloadLocks.GetOrAdd(accountLockKey, _ => new SemaphoreSlim(1, 1));

            try
            {
                // 等待获取锁
                await accountSemaphore.WaitAsync();

                try
                {
                    bool needRefresh = forceRefresh ||
                                       string.IsNullOrEmpty(account.CachedSkinPath) ||
                                       !File.Exists(account.CachedSkinPath) ||
                                       account.SkinLastUpdated == null ||
                                       DateTime.Now - account.SkinLastUpdated.Value > TimeSpan.FromDays(7);

                    if (!needRefresh && !string.IsNullOrEmpty(account.CachedSkinPath))
                    {
                        return account.CachedSkinPath;
                    }

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
                            return GetDefaultSkinPath();
                    }

                    if (string.IsNullOrEmpty(skinUrl)) return null;

                    var skinPath = await DownloadSkinAsync(account.Id, skinUrl);
                    if (string.IsNullOrEmpty(skinPath)) return null;

                    account.SkinUrl = skinUrl;
                    account.CachedSkinPath = skinPath;
                    account.SkinLastUpdated = DateTime.Now;

                    ObsMCLauncher.Core.Services.Accounts.AccountService.Instance.SaveAccountsData();

                    return skinPath;
                }
                finally
                {
                    accountSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("SkinService", $"获取皮肤失败: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetMicrosoftSkinUrlAsync(GameAccount account)
        {
            try
            {
                if (string.IsNullOrEmpty(account.MinecraftUUID)) return null;
                var uuid = account.MinecraftUUID.Replace("-", "");
                var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
                DebugLogger.Info("SkinService", $"获取微软账号皮肤: {uuid}");

                var response = await _httpClient.GetStringAsync(url);
                var profileData = JsonSerializer.Deserialize<JsonElement>(response);

                if (profileData.TryGetProperty("properties", out var properties))
                {
                    foreach (var property in properties.EnumerateArray())
                    {
                        if (property.TryGetProperty("name", out var name) && name.GetString() == "textures")
                        {
                            try
                            {
                                var base64Value = property.GetProperty("value").GetString() ?? "";
                                if (string.IsNullOrWhiteSpace(base64Value))
                                {
                                    DebugLogger.Warn("SkinService", "Base64 textures 值为空");
                                    continue;
                                }

                                // 清理 Base64 字符串（移除空白字符）
                                base64Value = base64Value.Trim();

                                var texturesJson = System.Text.Encoding.UTF8.GetString(
                                    Convert.FromBase64String(base64Value)
                                );
                                var texturesData = JsonSerializer.Deserialize<JsonElement>(texturesJson);
                                if (texturesData.TryGetProperty("textures", out var textures) &&
                                    textures.TryGetProperty("SKIN", out var skin) &&
                                    skin.TryGetProperty("url", out var urlProperty))
                                {
                                    var skinUrl = urlProperty.GetString();
                                    if (!string.IsNullOrEmpty(skinUrl))
                                    {
                                        DebugLogger.Info("SkinService", $"微软账号皮肤URL: {skinUrl}");
                                        return skinUrl;
                                    }
                                }
                            }
                            catch (FormatException ex)
                            {
                                DebugLogger.Error("SkinService", $"Base64解码失败: {ex.Message}");
                            }
                            catch (Exception ex)
                            {
                                DebugLogger.Error("SkinService", $"解析纹理数据失败: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Warn("SkinService", $"获取微软账号皮肤失败: {ex.Message}");
            }
            return null;
        }

        private async Task<string?> GetYggdrasilSkinUrlAsync(GameAccount account)
        {
            try
            {
                if (string.IsNullOrEmpty(account.YggdrasilServerId) || string.IsNullOrEmpty(account.UUID)) return null;
                var server = YggdrasilServerService.Instance.GetServerById(account.YggdrasilServerId);
                if (server == null)
                {
                    DebugLogger.Warn("SkinService", $"未找到外置服务器: {account.YggdrasilServerId}");
                    return null;
                }

                var uuid = account.UUID.Replace("-", "");
                var baseUrl = server.GetFullApiUrl();
                var url = $"{baseUrl.TrimEnd('/')}/sessionserver/session/minecraft/profile/{uuid}";

                DebugLogger.Info("SkinService", $"获取外置账号皮肤: {url}");

                var response = await _httpClient.GetStringAsync(url);
                var profileData = JsonSerializer.Deserialize<JsonElement>(response);

                if (profileData.TryGetProperty("properties", out var properties))
                {
                    foreach (var property in properties.EnumerateArray())
                    {
                        if (property.TryGetProperty("name", out var name) && name.GetString() == "textures")
                        {
                            try
                            {
                                var base64Value = property.GetProperty("value").GetString() ?? "";
                                if (string.IsNullOrWhiteSpace(base64Value))
                                {
                                    DebugLogger.Warn("SkinService", "Base64 textures 值为空");
                                    continue;
                                }

                                // 清理 Base64 字符串（移除空白字符）
                                base64Value = base64Value.Trim();

                                var texturesJson = System.Text.Encoding.UTF8.GetString(
                                    Convert.FromBase64String(base64Value)
                                );

                                DebugLogger.Info("SkinService", $"解析的纹理JSON: {texturesJson}");

                                var texturesData = JsonSerializer.Deserialize<JsonElement>(texturesJson);
                                if (texturesData.TryGetProperty("textures", out var textures) &&
                                    textures.TryGetProperty("SKIN", out var skin) &&
                                    skin.TryGetProperty("url", out var urlProperty))
                                {
                                    var skinUrl = urlProperty.GetString();
                                    if (!string.IsNullOrEmpty(skinUrl))
                                    {
                                        DebugLogger.Info("SkinService", $"外置账号皮肤URL: {skinUrl}");
                                        return skinUrl;
                                    }
                                }

                                DebugLogger.Warn("SkinService", "纹理数据中未找到SKIN.url字段");
                            }
                            catch (FormatException ex)
                            {
                                DebugLogger.Error("SkinService", $"Base64解码失败: {ex.Message}");
                            }
                            catch (Exception ex)
                            {
                                DebugLogger.Error("SkinService", $"解析纹理数据失败: {ex.Message}");
                            }
                        }
                    }
                }

                DebugLogger.Warn("SkinService", "外置账号皮肤获取失败: 未找到皮肤信息");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("SkinService", $"获取外置账号皮肤失败: {ex.Message}");
            }
            return null;
        }

        private string GetDefaultSkinPath()
        {
            var defaultSkinPath = Path.Combine(_skinCacheDir, "default_logo.png");
            
            if (File.Exists(defaultSkinPath))
            {
                return defaultSkinPath;
            }

            CreateDefaultSkinFile(defaultSkinPath);
            return File.Exists(defaultSkinPath) ? defaultSkinPath : string.Empty;
        }

        private void CreateDefaultSkinFile(string outputPath)
        {
            try
            {
                var assembly = typeof(SkinService).Assembly;
                var resourceName = "ObsMCLauncher.Core.Assets.default_skin.png";
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var fileStream = File.Create(outputPath);
                    stream.CopyTo(fileStream);
                    DebugLogger.Info("SkinService", $"已提取默认皮肤: {outputPath}");
                    return;
                }
                
                CreateFallbackDefaultSkin(outputPath);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("SkinService", $"提取默认皮肤失败: {ex.Message}");
                CreateFallbackDefaultSkin(outputPath);
            }
        }

        private void CreateFallbackDefaultSkin(string outputPath)
        {
            try
            {
                var steveSkinBase64 = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAXHSURBVHhe7ZtNbxNHGMf/M+u1ndiJnZAXCIRAKkJQoapQVVCkSxUqunDhR7AUKlSoULwIiRIFUqRIFCpSxIv4VRAqQoEKIVCugIRAcCTO1dzG7Lq7t3fZj2N2Z3Z2Zmd2ZnbFvq/n8Pb6zuzO7M7svfP0+zzf533u8/1cY9d1dA2gEjACaAJ0BHoCNdC3gAfQDmgGnYEeQDPoBjQD7Yc+A0fgF9AO+gP0BzqATkA/0Bz6DjQA+gONgV5Ao6tPwC1oB/QFugP9gU5AM6Ab0Av0BHoFPUJ/BKqgDdAW6A/2AfkA/oBvQC+gM9AE6Bb2AHkCP0F+hC9AV6AD0BHoEvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvUJ/BKugB9AW6A/0BfoFvvNkL/AA9yN6sAAAAASUVORK5CYII=";
                var skinData = Convert.FromBase64String(steveSkinBase64);
                File.WriteAllBytes(outputPath, skinData);
                DebugLogger.Info("SkinService", $"已创建默认皮肤: {outputPath}");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("SkinService", $"创建默认皮肤失败: {ex.Message}");
            }
        }

        private async Task<string?> DownloadSkinAsync(string accountId, string skinUrl)
        {
            var skinPath = Path.Combine(_skinCacheDir, $"{accountId}.png");

            // 如果文件已存在且有效，直接返回
            if (File.Exists(skinPath))
            {
                try
                {
                    // 检查文件是否有效（大小大于0）
                    var fileInfo = new FileInfo(skinPath);
                    if (fileInfo.Length > 0)
                    {
                        DebugLogger.Info("SkinService", $"皮肤文件已存在，跳过下载: {skinPath}");
                        return skinPath;
                    }
                }
                catch
                {
                    // 忽略检查错误，继续下载
                }
            }

            // 获取或创建该账号的下载锁
            var semaphore = _downloadLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

            try
            {
                // 等待获取锁，最多等待10秒
                if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(10)))
                {
                    DebugLogger.Warn("SkinService", $"获取下载锁超时: {accountId}");
                    return null;
                }

                try
                {
                    // 再次检查文件是否已被其他线程下载
                    if (File.Exists(skinPath))
                    {
                        var fileInfo = new FileInfo(skinPath);
                        if (fileInfo.Length > 0)
                        {
                            DebugLogger.Info("SkinService", $"皮肤文件已被其他线程下载: {skinPath}");
                            return skinPath;
                        }
                    }

                    DebugLogger.Info("SkinService", $"下载皮肤: {skinUrl}");

                    var skinData = await _httpClient.GetByteArrayAsync(skinUrl);

                    // 使用临时文件，下载完成后再重命名，避免文件冲突
                    var tempPath = Path.Combine(_skinCacheDir, $"{accountId}.tmp");

                    // 如果临时文件存在，先删除
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }

                    await File.WriteAllBytesAsync(tempPath, skinData);

                    // 如果目标文件存在，先删除
                    if (File.Exists(skinPath))
                    {
                        File.Delete(skinPath);
                    }

                    // 重命名临时文件为最终文件
                    File.Move(tempPath, skinPath);

                    DebugLogger.Info("SkinService", $"皮肤下载完成: {skinPath}");
                    return skinPath;
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error("SkinService", $"皮肤下载失败: {ex.Message}");
                return null;
            }
        }
    }
}
