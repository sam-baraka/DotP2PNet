using Dotp2pNet.Core.Models;

namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Orchestrates the entire torrent download and seeding process.
/// </summary>
/// <remarks>
/// The TorrentEngine is the main coordinator that ties together discovery,
/// networking, and storage layers. It manages the lifecycle of torrents,
/// coordinates piece selection and peer management, and tracks overall status.
/// </remarks>
public interface ITorrentEngine
{
    /// <summary>
    /// Starts downloading a torrent.
    /// </summary>
    /// <param name="metadata">The torrent metadata containing file info and hashes.</param>
    /// <param name="savePath">The directory where files should be saved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method:
    /// 1. Announces to trackers to get peer list
    /// 2. Performs DHT lookup for additional peers
    /// 3. Connects to available peers
    /// 4. Begins coordinating piece downloads
    /// 5. Tracks progress and manages peer connections
    /// </remarks>
    Task StartDownloadAsync(TorrentMetadata metadata, string savePath, CancellationToken ct = default);

    /// <summary>
    /// Starts seeding a complete file.
    /// </summary>
    /// <param name="metadata">The torrent metadata.</param>
    /// <param name="filePath">The path to the complete file to seed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method:
    /// 1. Verifies the file is complete and matches the metadata
    /// 2. Announces to trackers as a seeder
    /// 3. Announces to DHT
    /// 4. Accepts incoming connections and serves pieces
    /// </remarks>
    Task StartSeedingAsync(TorrentMetadata metadata, string filePath, CancellationToken ct = default);

    /// <summary>
    /// Pauses an active torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent to pause.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Pausing stops all piece transfers but maintains peer connections.
    /// The torrent can be resumed later without losing progress.
    /// </remarks>
    Task PauseAsync(byte[] infoHash, CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Resuming restarts piece transfers and may reconnect to peers.
    /// </remarks>
    Task ResumeAsync(byte[] infoHash, CancellationToken ct = default);

    /// <summary>
    /// Stops a torrent and removes it from the engine.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent to stop.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Stopping closes all peer connections and announces to trackers.
    ///   /// Downloaded reserved on disk.
    /// </remarks>
    Task StopAsync(byte[] infoHash, CancellationToken ct = default);

    /// <summary>
    /// Gets the current status of a torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    /// <returns>The current status, or null if the torrent is not found.</returns>
    TorrentStatus? GetStatus(byte[] infoHash);

    /// <summary>
    /// Gets the status of all active torrents.
    /// </summary>
    /// <returns>A list of all torrent statuses.</returns>
    List<TorrentStatus> GetAllStatuses();
}
