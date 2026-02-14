using System;
using System.Collections.Generic;
using System.Linq;

namespace ObsMCLauncher.Core.Models;

public class ModTranslation
{
    public string CurseForgeId { get; set; } = string.Empty;

    public List<string> ModIds { get; set; } = new();

    public string ChineseName { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;

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

    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(Abbreviation))
            return $"[{Abbreviation}] {ChineseName}";
        return ChineseName;
    }
}
