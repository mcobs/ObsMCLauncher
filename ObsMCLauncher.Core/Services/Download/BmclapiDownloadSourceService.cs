namespace ObsMCLauncher.Core.Services.Download;

public class BmclapiDownloadSourceService : IDownloadSourceService
{
    public string GetAssetUrl(string hash)
        => $"https://bmclapi2.bangbang93.com/assets/{hash.Substring(0, 2)}/{hash}";

    public string GetVersionManifestUrl()
        => "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json";

    public string GetVersionJsonUrl(string versionId)
        => $"https://bmclapi2.bangbang93.com/version/{versionId}/json";

    public string GetLibraryUrl(string libraryPath)
        => $"https://bmclapi2.bangbang93.com/maven/{libraryPath}";
}
