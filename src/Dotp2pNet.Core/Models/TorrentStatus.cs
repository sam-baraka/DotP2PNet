using System.ComponentModel.DataAnnotations;

namespace Dotp2pNet.Core.Models;

/// <summary>
/// Represents the current status of a torrent.
/// </summary>
/// <remarks>
/// Provides a snapshot of the torrent's state including download progress,
/// peer connections, and transfer rates. Used for monitoring and display.
/// </remarks>
public class TorrentStatus
{
    /// <summary>
    /// Gets or sets the SHA-1 hash that uniquely identifies this torrent.
    /// </summary>
    [Required]
    public byte[] InfoHash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the current state of the torrent.
    /// </summary>
    public TorrentState State { get; set; } = TorrentState.Stopped;

    /// <summary>
    /// Gets or sets the number of bytes downloaded so far.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long Downloaded { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes uploaded so far.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long Uploaded { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes remaining to download.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long Remaining { get; set; }

    /// <summary>
    /// Gets or sets the total size of the torrent in bytes.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long TotalSize { get; set; }

    /// <summary>
    /// Gets or sets the download progress as a percentage (0-100).
    /// </summary>
    [Range(0, 100)]
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the number of peers currently connected.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int ConnectedPeers { get; set; }

    /// <summary>
    /// Gets or sets the total number of peers available in the swarm.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int AvailablePeers { get; set; }

    /// <summary>
    /// Gets or sets the number of seeders (peers with complete file) in the swarm.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int Seeders { get; set; }

    /// <summary>
    /// Gets or sets the number of leechers (peers downloading) in the swarm.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int Leechers { get; set; }

    /// <summary>
    /// Gets or sets the current download rate in bytes per second.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long DownloadRate { get; set; }

    /// <summary>
    /// Gets or sets the current upload rate in bytes per second.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long UploadRate { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining to complete the download.
    /// Null if the torrent is complete or the rate is zero.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the torrent was added.
    /// </summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp when the torrent was completed.
    /// Null if not yet complete.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the name of the torrent.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the save path for the downloaded files.
    /// </summary>
    public string SavePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets any error message if the torrent is in an error state.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets a value indicating whether the torrent is complete.
    /// </summary>
    public bool IsComplete => Progress >= 100 || Remaining == 0;

    /// <summary>
    /// Gets a value indicating whether the torrent is actively downloading.
    /// </summary>
    public bool IsDownloading => State == TorrentState.Downloading && DownloadRate > 0;

    /// <summary>
    /// Gets a value indicating whether the torrent is actively seeding.
    /// </summary>
    public bool IsSeeding => State == TorrentState.Seeding && UploadRate > 0;

    /// <summary>
    /// Gets the share ratio (uploaded / downloaded).
    /// Returns 0 if nothing has been downloaded yet.
    /// </summary>
    public double ShareRatio
    {
        get
        {
            if (Downloaded == 0)
                return 0;

            return (double)Uploaded / Downloaded;
        }
    }

    /// <summary>
    /// Gets the total time elapsed since the torrent was added.
    /// </summary>
    public TimeSpan TotalTime => DateTime.UtcNow - AddedAt;

    /// <summary>
    /// Gets the average download rate over the entire session.
    /// </summary>
    public double AverageDownloadRate
    {
        get
        {
            var seconds = TotalTime.TotalSeconds;
            if (seconds < 1)
                return 0;

            return Downloaded / seconds;
        }
    }

    /// <summary>
    /// Gets the average upload rate over the entire session.
    /// </summary>
    public double AverageUploadRate
    {
        get
        {
            var seconds = TotalTime.TotalSeconds;
            if (seconds < 1)
                return 0;

            return Uploaded / seconds;
        }
    }

    /// <summary>
    /// Updates the progress percentage based on downloaded and total size.
    /// </summary>
    public void UpdateProgress()
    {
        if (TotalSize > 0)
        {
            Progress = Math.Min(100, (double)Downloaded / TotalSize * 100);
            Remaining = Math.Max(0, TotalSize - Downloaded);
        }
    }

    /// <summary>
    /// Updates the estimated time remaining based on current download rate.
    /// </summary>
    public void UpdateEstimatedTime()
    {
        if (Remaining > 0 && DownloadRate > 0)
        {
            var secondsRemaining = Remaining / (double)DownloadRate;
            EstimatedTimeRemaining = TimeSpan.FromSeconds(secondsRemaining);
        }
        else
        {
            EstimatedTimeRemaining = null;
        }
    }

    /// <summary>
    /// Marks the torrent as complete.
    /// </summary>
    public void MarkComplete()
    {
        Progress = 100;
        Remaining = 0;
        Downloaded = TotalSize;
        CompletedAt = DateTime.UtcNow;
        State = TorrentState.Seeding;
        EstimatedTimeRemaining = null;
    }

    /// <summary>
    /// Validates the torrent status.
    /// </summary>
    /// <returns>True if all fields are valid; otherwise, false.</returns>
    public bool Validate()
    {
        if (InfoHash.Length != 20)
            return false;

        if (string.IsNullOrWhiteSpace(Name))
            return false;

        if (TotalSize <= 0)
            return false;

        if (Downloaded < 0 || Uploaded < 0 || Remaining < 0)
            return false;

        if (Progress < 0 || Progress > 100)
            return false;

        if (ConnectedPeers < 0 || AvailablePeers < 0)
            return false;

        if (DownloadRate < 0 || UploadRate < 0)
            return false;

        return true;
    }

    /// <summary>
    /// Returns a string representation of the torrent status.
    /// </summary>
    /// <returns>A formatted string showing key status information.</returns>
    public override string ToString()
    {
        return $"{Name}: {Progress:F1}% ({FormatBytes(Downloaded)}/{FormatBytes(TotalSize)}) " +
               $"↓ {FormatRate(DownloadRate)} ↑ {FormatRate(UploadRate)} " +
               $"Peers: {ConnectedPeers}/{AvailablePeers} State: {State}";
    }

    /// <summary>
    /// Formats a byte count into a human-readable string.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>A formatted string (e.g., "1.5 GB").</returns>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }

    /// <summary>
    /// Formats a rate (bytes per second) into a human-readable string.
    /// </summary>
    /// <param name="bytesPerSecond">The rate in bytes per second.</param>
    /// <returns>A formatted string (e.g., "2.5 MB/s").</returns>
    private static string FormatRate(long bytesPerSecond)
    {
        return $"{FormatBytes(bytesPerSecond)}/s";
    }
}

/// <summary>
/// Defines the possible states of a torrent.
/// </summary>
/// <remarks>
/// The torrent progresses through these states during its lifecycle:
/// Stopped → Starting → Downloading → Seeding
/// It can be Paused at any time or enter Error state on failure.
/// </remarks>
public enum TorrentState
{
    /// <summary>
    /// The torrent is stopped and not active.
    /// </summary>
    Stopped,

    /// <summary>
    /// The torrent is starting up (connecting to peers, loading state).
    /// </summary>
    Starting,

    /// <summary>
    /// The torrent is actively downloading pieces.
    /// </summary>
    Downloading,

    /// <summary>
    /// The torrent is complete and seeding to other peers.
    /// </summary>
    Seeding,

    /// <summary>
    /// The torrent is paused by the user.
    /// </summary>
    Paused,

    /// <summary>
    /// The torrent encountered an error and cannot continue.
    /// </summary>
    Error
}
