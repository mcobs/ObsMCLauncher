namespace ObsMCLauncher.Core.Services.Download;

public class MojangDownloadSourceService : IDownloadSourceService
{
    public string GetAssetUrl(string hash)
        => $"https://resources.download.minecraft.net/{hash.Substring(0, 2)}/{hash}";

    public string GetVersionManifestUrl()
        => "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    public string GetVersionJsonUrl(string versionId)
        => $"https://piston-meta.mojang.com/v1/packages/{versionId}.json";

    public string GetLibraryUrl(string libraryPath)
        => $"https://libraries.minecraft.net/{libraryPath}";
}
