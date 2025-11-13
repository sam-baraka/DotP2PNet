using System.ComponentModel.DataAnnotations;

namespace Dotp2pNet.Core.Models;

/// <summary>
/// Represents metadata about a torrent file.
/// </summary>
/// <remarks>
/// Contains all information needed to download and verify a file in the P2P network.
/// The InfoHash uniquely identifies the torrent and is used for peer discovery.
/// PieceHashes are used to verify the integrity of downloaded pieces.
/// </remarks>
public class TorrentMetadata
{
    /// <summary>
    /// Gets or sets the SHA-1 hash of the info dictionary.
    /// This uniquely identifies the torrent.
    /// </summary>
    [Required]
    public byte[] InfoHash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the name of the file or directory.
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total size of all files in bytes.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long TotalSize { get; set; }

    /// <summary>
    /// Gets or sets the size of each piece in bytes.
    /// Typically 256 KB (262,144 bytes) for optimal performance.
    /// </summary>
    [Range(16384, 16777216)] // 16 KB to 16 MB
    public int PieceSize { get; set; }

    /// <summary>
    /// Gets or sets the list of SHA-256 hashes for each piece.
    /// Used to verify piece integrity after download.
    /// </summary>
    [Required]
    public List<byte[]> PieceHashes { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of files in the torrent.
    /// For single-file torrents, this contains one entry.
    /// </summary>
    [Required]
    public List<TorrentFileInfo> Files { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of tracker URLs.
    /// Trackers are used for centralized peer discovery.
    /// </summary>
    public List<string> Trackers { get; set; } = new();

    /// <summary>
    /// Gets the total number of pieces in the torrent.
    /// </summary>
    public int PieceCount => PieceHashes.Count;

    /// <summary>
    /// Validates the torrent metadata.
    /// </summary>
    /// <returns>True if all required fields are valid; otherwise, false.</returns>
    public bool Validate()
    {
        if (InfoHash.Length != 20)
            return false;

        if (string.IsNullOrWhiteSpace(Name))
            return false;

        if (TotalSize <= 0)
            return false;

        if (PieceSize < 16384 || PieceSize > 16777216)
            return false;

        if (PieceHashes.Count == 0)
            return false;

        // Verify all piece hashes are 32 bytes (SHA-256)
        if (PieceHashes.Any(hash => hash.Length != 32))
            return false;

        if (Files.Count == 0)
            return false;

        // Verify total size matches sum of file sizes
        var calculatedSize = Files.Sum(f => f.Length);
        if (calculatedSize != TotalSize)
            return false;

        return true;
    }
}

/// <summary>
/// Represents information about a file within a torrent.
/// </summary>
/// <remarks>
/// For single-file torrents, there is one TorrentFileInfo.
/// For multi-file torrents, each file has its own TorrentFileInfo.
/// </remarks>
public class TorrentFileInfo
{
    /// <summary>
    /// Gets or sets the path components of the file.
    /// For single-file torrents, this is just the filename.
    /// For multi-file torrents, this includes directory structure.
    /// </summary>
    [Required]
    public List<string> Path { get; set; } = new();

    /// <summary>
    /// Gets or sets the length of the file in bytes.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long Length { get; set; }

    /// <summary>
    /// Gets the full path of the file by joining path components.
    /// </summary>
    public string FullPath => string.Join(System.IO.Path.DirectorySeparatorChar, Path);

    /// <summary>
    /// Validates the file information.
    /// </summary>
    /// <returns>True if the path is not empty and length is non-negative; otherwise, false.</returns>
    public bool Validate()
    {
        if (Path.Count == 0)
            return false;

        if (Path.Any(string.IsNullOrWhiteSpace))
            return false;

        if (Length < 0)
            return false;

        return true;
    }
}
