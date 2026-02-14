using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services;

public class ModTranslationService
{
    private static readonly Lazy<ModTranslationService> _instance = new(() => new ModTranslationService());
    public static ModTranslationService Instance => _instance.Value;

    private readonly List<ModTranslation> _translations = new();
    private readonly Dictionary<string, ModTranslation> _curseForgeIdMap = new();
    private readonly Dictionary<string, ModTranslation> _modIdMap = new();
    private bool _isLoaded;

    private ModTranslationService()
    {
        LoadTranslations();
    }

    private void LoadTranslations()
    {
        try
        {
            string[]? lines = null;

            var userTranslationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "mod_translations.txt");
            if (File.Exists(userTranslationPath))
            {
                Debug.WriteLine($"[ModTranslation] 从用户目录读取翻译: {userTranslationPath}");
                lines = File.ReadAllLines(userTranslationPath, Encoding.UTF8);
            }

            if (lines == null)
            {
                lines = LoadFromEmbeddedResource();
            }

            if (lines == null)
            {
                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "mod_translations.txt"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Assets", "mod_translations.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "mod_translations.txt")
                };

                foreach (var path in possiblePaths)
                {
                    var normalizedPath = Path.GetFullPath(path);
                    if (File.Exists(normalizedPath))
                    {
                        Debug.WriteLine($"[ModTranslation] 从备用路径找到翻译文件: {normalizedPath}");
                        lines = File.ReadAllLines(normalizedPath, Encoding.UTF8);
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
                if (translation == null)
                    continue;

                _translations.Add(translation);

                var curseForgeIds = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrWhiteSpace(id));

                foreach (var cfId in curseForgeIds)
                {
                    _curseForgeIdMap.TryAdd(cfId.ToLowerInvariant(), translation);
                }

                foreach (var modId in translation.ModIds)
                {
                    _modIdMap.TryAdd(modId.ToLowerInvariant(), translation);
                }
            }

            _isLoaded = true;
            Debug.WriteLine($"[ModTranslation] 加载了 {_translations.Count} 条翻译数据");
            Debug.WriteLine($"[ModTranslation] CurseForge ID索引: {_curseForgeIdMap.Count} 个");
            Debug.WriteLine($"[ModTranslation] Mod ID索引: {_modIdMap.Count} 个");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModTranslation] 加载翻译失败: {ex.Message}");
            _isLoaded = false;
        }
    }

    public ModTranslation? GetTranslationByCurseForgeId(string curseForgeId)
    {
        if (string.IsNullOrWhiteSpace(curseForgeId))
            return null;

        _curseForgeIdMap.TryGetValue(curseForgeId.ToLowerInvariant(), out var translation);
        return translation;
    }

    public ModTranslation? GetTranslationByCurseForgeId(int curseForgeId)
    {
        return GetTranslationByCurseForgeId(curseForgeId.ToString());
    }

    public ModTranslation? GetTranslationByModId(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return null;

        _modIdMap.TryGetValue(modId.ToLowerInvariant(), out var translation);
        return translation;
    }

    public string GetDisplayName(string originalName, ModTranslation? translation)
    {
        if (translation == null || string.IsNullOrWhiteSpace(translation.ChineseName))
            return originalName;

        if (translation.ChineseName.Contains(originalName, StringComparison.OrdinalIgnoreCase))
            return translation.GetDisplayName();

        return $"{originalName} ({translation.GetDisplayName()})";
    }

    public bool MatchesSearch(string originalName, ModTranslation? translation, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return true;

        searchText = searchText.ToLowerInvariant();

        if (originalName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            return true;

        if (translation != null)
        {
            if (translation.ChineseName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(translation.Abbreviation) &&
                translation.Abbreviation.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                return true;

            if (translation.ModIds.Any(id => id.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public void Reload()
    {
        LoadTranslations();
    }

    public bool IsLoaded => _isLoaded;

    public IReadOnlyList<ModTranslation> GetAllTranslations() => _translations.AsReadOnly();

    private string[]? LoadFromEmbeddedResource()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            var resourceNames = new[]
            {
                "ObsMCLauncher.Assets.mod_translations.txt",
                "Assets.mod_translations.txt",
                "mod_translations.txt"
            };

            foreach (var resourceName in resourceNames)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                Debug.WriteLine($"[ModTranslation] 从嵌入资源加载: {resourceName}");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = reader.ReadToEnd();
                return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            }

            var allResources = assembly.GetManifestResourceNames();
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
