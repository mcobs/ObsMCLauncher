namespace ObsMCLauncher.Core.Services;

public static class DownloadUrls
{
    public static string GetVersionManifestUrl()
    {
        // 先用官方源，后续再接入下载源管理（BMCLAPI/MCBBS等）
        return "https://launchermeta.mojang.com/mc/game/version_manifest.json";
    }
}
