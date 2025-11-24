using Dotp2pNet.Core.Models;

namespace Dotp2pNet.Storage.State;

/// <summary>
/// Represents the persisted state of a torrent download.
/// </summary>
/// <remarks>
/// This class contains all information needed to resume a download after
/// the application restarts. It includes the torrent metadata, which pieces
/// have been downloaded, and download statistics.
/// </remarks>
public class DownloadState
{
    /// <summary>
    /// Gets or sets the torrent metadata.
    /// </summary>
    public TorrentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the bitfield as a byte array.
    /// </summary>
    /// <remarks>
    /// The bitfield is serialized to bytes for JSON storage.
    /// Each bit represents whether a piece has been downloaded.
    /// </remarks>
    public byte[] BitfieldBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the number of pieces in the bitfield.
    /// </summary>
    /// <remarks>
    /// Needed because the bitfield byte array may have padding bits.
    /// </remarks>
    public int PieceCount { get; set; }

    /// <summary>
    /// Gets or sets the download directory path.
    /// </summary>
    public string DownloadDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total bytes downloaded.
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Gets or sets the total bytes uploaded.
    /// </summary>
    public long BytesUploaded { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the download was last active.
    /// </summary>
    public DateTime LastActive { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp when the download was added.
    /// </summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the torrent state.
    /// </summary>
    public TorrentState State { get; set; } = TorrentState.Stopped;

    /// <summary>
    /// Validates the download state.
    /// </summary>
    /// <returns>True if the state is valid; otherwise, false.</returns>
    public bool Validate()
    {
        if (Metadata == null || !Metadata.Validate())
            return false;

        if (BitfieldBytes == null || BitfieldBytes.Length == 0)
            return false;

        if (PieceCount <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(DownloadDirectory))
            return false;

        if (BytesDownloaded < 0 || BytesUploaded < 0)
            return false;

        return true;
    }
}
