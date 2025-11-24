namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Manages persistence of download state to disk.
/// </summary>
/// <remarks>
/// The StateManager is responsible for saving and loading download state,
/// enabling resume capability after application restart. State is stored
/// in JSON format in the user's data directory.
/// </remarks>
public interface IStateManager
{
    /// <summary>
    /// Saves the current download state to disk.
    /// </summary>
    /// <param name="infoHash">The torrent's info hash (used as filename).</param>
    /// <param name="metadata">The torrent metadata.</param>
    /// <param name="bitfield">The current bitfield showing which pieces are downloaded.</param>
    /// <param name="downloadDirectory">The directory where files are being downloaded.</param>
    /// <param name="bytesDownloaded">Total bytes downloaded.</param>
    /// <param name="bytesUploaded">Total bytes uploaded.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveStateAsync(
        byte[] infoHash,
        Core.Models.TorrentMetadata metadata,
        IBitfield bitfield,
        string downloadDirectory,
        long bytesDownloaded,
        long bytesUploaded,
        CancellationToken ct = default);

    /// <summary>
    /// Loads download state from disk.
    /// </summary>
    /// <param name="infoHash">The torrent's info hash (used as filename).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains
    /// the loaded download state, or null if no state file exists.
    /// </returns>
    Task<Storage.State.DownloadState?> LoadStateAsync(
        byte[] infoHash,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the state file for a torrent.
    /// </summary>
    /// <param name="infoHash">The torrent's info hash (used as filename).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteStateAsync(
        byte[] infoHash,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a state file exists for a torrent.
    /// </summary>
    /// <param name="infoHash">The torrent's info hash (used as filename).</param>
    /// <returns>True if a state file exists; otherwise, false.</returns>
    bool StateExists(byte[] infoHash);

    /// <summary>
    /// Gets all saved torrent info hashes.
    /// </summary>
    /// <returns>A list of info hashes for all saved torrents.</returns>
    Task<List<byte[]>> GetAllSavedTorrentsAsync(CancellationToken ct = default);
}
