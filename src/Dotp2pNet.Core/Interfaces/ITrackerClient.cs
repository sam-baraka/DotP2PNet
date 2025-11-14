namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Represents the type of event being reported to the tracker.
/// </summary>
public enum TrackerEvent
{
    /// <summary>
    /// No event (regular announce).
    /// </summary>
    None = 0,

    /// <summary>
    /// The download has started.
    /// </summary>
    Started = 1,

    /// <summary>
    /// The download has completed.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The download has stopped.
    /// </summary>
    Stopped = 3
}

/// <summary>
/// Represents a response from a tracker announce request.
/// </summary>
public class TrackerResponse
{
    /// <summary>
    /// Gets or sets the interval in seconds between regular announces.
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Gets or sets the minimum interval in seconds between announces.
    /// </summary>
    public int? MinInterval { get; set; }

    /// <summary>
    /// Gets or sets the tracker ID for this client.
    /// </summary>
    public string? TrackerId { get; set; }

    /// <summary>
    /// Gets or sets the number of seeders (peers with complete file).
    /// </summary>
    public int Complete { get; set; }

    /// <summary>
    /// Gets or sets the number of leechers (peers downloading).
    /// </summary>
    public int Incomplete { get; set; }

    /// <summary>
    /// Gets or sets the list of peers returned by the tracker.
    /// </summary>
    public List<Models.PeerInfo> Peers { get; set; } = new();

    /// <summary>
    /// Gets or sets an optional warning message from the tracker.
    /// </summary>
    public string? WarningMessage { get; set; }
}

/// <summary>
/// Communicates with HTTP trackers for centralized peer discovery.
/// </summary>
/// <remarks>
/// Implements the BitTorrent HTTP tracker protocol (BEP 3).
/// Trackers maintain lists of peers participating in file sharing
/// and coordinate peer discovery through announce requests.
/// </remarks>
public interface ITrackerClient
{
    /// <summary>
    /// Announces to the tracker and retrieves peer list.
    /// </summary>
    /// <param name="trackerUrl">The URL of the tracker.</param>
    /// <param name="infoHash">The info hash of the torrent (20 bytes).</param>
    /// <param name="peerId">The peer ID of this client (20 bytes).</param>
    /// <param name="port">The port this client is listening on.</param>
    /// <param name="downloaded">Total bytes downloaded so far.</param>
    /// <param name="uploaded">Total bytes uploaded so far.</param>
    /// <param name="left">Bytes remaining to download.</param>
    /// <param name="eventType">The type of event being reported.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tracker response containing peer list and announce interval.</returns>
    Task<TrackerResponse> AnnounceAsync(
        string trackerUrl,
        byte[] infoHash,
        byte[] peerId,
        int port,
        long downloaded,
        long uploaded,
        long left,
        TrackerEvent eventType,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the list of peers for a torrent from the tracker.
    /// This is a convenience method that calls AnnounceAsync with no event.
    /// </summary>
    /// <param name="trackerUrl">The URL of the tracker.</param>
    /// <param name="infoHash">The info hash of the torrent (20 bytes).</param>
    /// <param name="peerId">The peer ID of this client (20 bytes).</param>
    /// <param name="port">The port this client is listening on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The list of peers sharing this torrent.</returns>
    Task<List<Models.PeerInfo>> GetPeersAsync(
        string trackerUrl,
        byte[] infoHash,
        byte[] peerId,
        int port,
        CancellationToken ct = default);
}
