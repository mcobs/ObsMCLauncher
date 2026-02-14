namespace ObsMCLauncher.Core.Services.Download;

public class DownloadProgress
{
    public long CurrentFileBytes { get; set; }
    public long CurrentFileTotalBytes { get; set; }
    public double CurrentFilePercentage => CurrentFileTotalBytes > 0 ? (CurrentFileBytes * 100.0 / CurrentFileTotalBytes) : 0;

    public long TotalDownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double OverallPercentage => TotalBytes > 0 ? (TotalDownloadedBytes * 100.0 / TotalBytes) : 0;

    public int CompletedFiles { get; set; }
    public int TotalFiles { get; set; }

    public string CurrentFile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double DownloadSpeed { get; set; }

    [System.Obsolete("Use CurrentFileBytes instead")]
    public long DownloadedBytes
    {
        get => CurrentFileBytes;
        set => CurrentFileBytes = value;
    }

    [System.Obsolete("Use CurrentFilePercentage instead")]
    public double ProgressPercentage => CurrentFilePercentage;
}
