namespace ObsMCLauncher.Core.Services.Download;

public interface IDownloadSourceService
{
    string GetVersionManifestUrl();

    string GetVersionJsonUrl(string versionId);

    string GetLibraryUrl(string libraryPath);

    string GetAssetUrl(string hash);
}
