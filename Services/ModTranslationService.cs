using ObsMCLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// MOD翻译服务
    /// </summary>
    public class ModTranslationService
    {
        private static readonly Lazy<ModTranslationService> _instance = new(() => new ModTranslationService());
        public static ModTranslationService Instance => _instance.Value;

        private List<ModTranslation> _translations = new();
        private Dictionary<string, ModTranslation> _curseForgeIdMap = new();
        private Dictionary<string, ModTranslation> _modIdMap = new();
        private bool _isLoaded = false;

        private ModTranslationService()
        {
            LoadTranslations();
        }

        /// <summary>
        /// 加载翻译数据
        /// </summary>
        private void LoadTranslations()
        {
            try
            {
                string[]? lines = null;

                // 优先从运行目录\OMCL\mod_translations.txt读取（用户可编辑）
                var userTranslationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "mod_translations.txt");
                if (File.Exists(userTranslationPath))
                {
                    Debug.WriteLine($"[ModTranslation] 从用户目录读取翻译: {userTranslationPath}");
                    lines = File.ReadAllLines(userTranslationPath, Encoding.UTF8);
                }
                
                // 如果用户目录没有，尝试从嵌入资源读取
                if (lines == null)
                {
                    lines = LoadFromEmbeddedResource();
                }
                
                // 如果嵌入资源读取失败，尝试从其他路径读取
                if (lines == null)
                {
                    var possiblePaths = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "mod_translations.txt"),
                        Path.Combine(Directory.GetCurrentDirectory(), "Assets", "mod_translations.txt"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "mod_translations.txt")
                    };

                    string? translationFile = null;
                    foreach (var path in possiblePaths)
                    {
                        var normalizedPath = Path.GetFullPath(path);
                        if (File.Exists(normalizedPath))
                        {
                            translationFile = normalizedPath;
                            Debug.WriteLine($"[ModTranslation] 从备用路径找到翻译文件: {translationFile}");
                            lines = File.ReadAllLines(translationFile, Encoding.UTF8);
                            break;
                        }
                    }
                }
                
                if (lines == null)
                {
                    Debug.WriteLine("[ModTranslation] 翻译文件不存在，跳过加载");
                    Debug.WriteLine($"[ModTranslation] 提示：翻译文件应位于 {userTranslationPath}");
                    _isLoaded = false;
                    return;
                }
                _translations.Clear();
                _curseForgeIdMap.Clear();
                _modIdMap.Clear();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('|');
                    if (parts.Length < 3)
                        continue;

                    var translation = ModTranslation.Parse(line);
                    if (translation != null)
                    {
                        _translations.Add(translation);

                        // 建立CurseForge ID索引（支持多个ID，用逗号分隔）
                        var curseForgeIds = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(id => id.Trim())
                            .Where(id => !string.IsNullOrWhiteSpace(id));

                        foreach (var cfId in curseForgeIds)
                        {
                            _curseForgeIdMap.TryAdd(cfId.ToLower(), translation);
                        }

                        // 建立Mod ID索引
                        foreach (var modId in translation.ModIds)
                        {
                            _modIdMap.TryAdd(modId.ToLower(), translation);
                        }
                    }
                }

                _isLoaded = true;
                Debug.WriteLine($"[ModTranslation] 加载了 {_translations.Count} 条翻译数据");
                Debug.WriteLine($"[ModTranslation] CurseForge ID索引: {_curseForgeIdMap.Count} 个");
                Debug.WriteLine($"[ModTranslation] Mod ID索引: {_modIdMap.Count} 个");
                
                // 输出前5个翻译作为示例
                foreach (var t in _translations.Take(5))
                {
                    Debug.WriteLine($"[ModTranslation] 示例: {t.CurseForgeId} -> {t.ChineseName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModTranslation] 加载翻译失败: {ex.Message}");
                _isLoaded = false;
            }
        }

        /// <summary>
        /// 根据CurseForge ID获取翻译
        /// </summary>
        public ModTranslation? GetTranslationByCurseForgeId(string curseForgeId)
        {
            if (string.IsNullOrWhiteSpace(curseForgeId))
                return null;

            var key = curseForgeId.ToLower();
            _curseForgeIdMap.TryGetValue(key, out var translation);
            
            return translation;
        }

        /// <summary>
        /// 根据CurseForge ID获取翻译（支持数字ID）
        /// </summary>
        public ModTranslation? GetTranslationByCurseForgeId(int curseForgeId)
        {
            return GetTranslationByCurseForgeId(curseForgeId.ToString());
        }

        /// <summary>
        /// 根据Mod ID获取翻译
        /// </summary>
        public ModTranslation? GetTranslationByModId(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return null;

            _modIdMap.TryGetValue(modId.ToLower(), out var translation);
            return translation;
        }

        /// <summary>
        /// 获取显示名称（如果有翻译则返回"英文名 (中文名)"格式）
        /// </summary>
        public string GetDisplayName(string originalName, ModTranslation? translation)
        {
            if (translation == null || string.IsNullOrWhiteSpace(translation.ChineseName))
                return originalName;

            // 如果中文名包含英文名，只返回中文名
            if (translation.ChineseName.Contains(originalName, StringComparison.OrdinalIgnoreCase))
                return translation.GetDisplayName();

            return $"{originalName} ({translation.GetDisplayName()})";
        }

        /// <summary>
        /// 搜索翻译（支持中英文）
        /// </summary>
        public bool MatchesSearch(string originalName, ModTranslation? translation, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            searchText = searchText.ToLower();

            // 匹配原始名称
            if (originalName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                return true;

            // 匹配翻译
            if (translation != null)
            {
                // 匹配中文名
                if (translation.ChineseName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    return true;

                // 匹配缩写
                if (!string.IsNullOrWhiteSpace(translation.Abbreviation) &&
                    translation.Abbreviation.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    return true;

                // 匹配Mod ID
                if (translation.ModIds.Any(id => id.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 重新加载翻译数据
        /// </summary>
        public void Reload()
        {
            LoadTranslations();
        }

        /// <summary>
        /// 是否已加载
        /// </summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// 获取所有翻译
        /// </summary>
        public IReadOnlyList<ModTranslation> GetAllTranslations() => _translations.AsReadOnly();

        /// <summary>
        /// 从嵌入资源加载翻译文件
        /// </summary>
        private string[]? LoadFromEmbeddedResource()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                
                // 尝试多个可能的资源名称格式
                var resourceNames = new[]
                {
                    "ObsMCLauncher.Assets.mod_translations.txt",
                    "Assets.mod_translations.txt",
                    "mod_translations.txt"
                };

                // 如果没有找到，列出所有资源名称用于调试
                var allResources = assembly.GetManifestResourceNames();
                
                foreach (var resourceName in resourceNames)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        Debug.WriteLine($"[ModTranslation] 从嵌入资源加载: {resourceName}");
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var content = reader.ReadToEnd();
                        return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    }
                }

                // 调试：输出所有可用的资源名称
                Debug.WriteLine("[ModTranslation] 未找到嵌入资源，可用的资源名称：");
                foreach (var name in allResources)
                {
                    Debug.WriteLine($"  - {name}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModTranslation] 从嵌入资源读取失败: {ex.Message}");
            }

            return null;
        }
    }
}

