using System;
using System.Collections.Generic;
using System.Linq;

namespace ObsMCLauncher.Models
{
    /// <summary>
    /// MOD翻译数据模型
    /// </summary>
    public class ModTranslation
    {
        /// <summary>
        /// CurseForge项目ID或Slug
        /// </summary>
        public string CurseForgeId { get; set; } = string.Empty;

        /// <summary>
        /// Mod ID列表（支持多个，用于匹配）
        /// </summary>
        public List<string> ModIds { get; set; } = new();

        /// <summary>
        /// 中文名称
        /// </summary>
        public string ChineseName { get; set; } = string.Empty;

        /// <summary>
        /// 缩写
        /// </summary>
        public string Abbreviation { get; set; } = string.Empty;

        /// <summary>
        /// 解析一行翻译数据
        /// 格式：curseforge_id|mod_ids|chinese_name|abbr
        /// 注意：curseforge_id也可以包含多个ID，用逗号分隔
        /// </summary>
        public static ModTranslation? Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                return null;

            var parts = line.Split('|');
            if (parts.Length < 3)
                return null;

            var modIds = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            // CurseForgeId可能包含多个，取第一个作为主ID
            var curseForgeId = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .FirstOrDefault() ?? "";

            return new ModTranslation
            {
                CurseForgeId = curseForgeId,
                ModIds = modIds,
                ChineseName = parts[2].Trim(),
                Abbreviation = parts.Length > 3 ? parts[3].Trim() : string.Empty
            };
        }

        /// <summary>
        /// 获取显示名称（包含缩写）
        /// </summary>
        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(Abbreviation))
                return $"[{Abbreviation}] {ChineseName}";
            return ChineseName;
        }
    }
}

