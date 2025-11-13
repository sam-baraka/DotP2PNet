using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Dotp2pNet.Core.Models;

/// <summary>
/// Represents information about a peer in the P2P network.
/// </summary>
/// <remarks>
/// Contains identifying information (PeerId, IP address, port) and
/// statistics about the peer's performance and behavior.
/// Used for peer selection and reputation tracking.
/// </remarks>
public class PeerInfo
{
    /// <summary>
    /// Gets or sets the unique identifier for this peer.
    /// Typically 20 bytes, often starting with client identifier.
    /// </summary>
    [Required]
    public byte[] PeerId { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the IP address of the peer.
    ///   /// </summary> [Required]
    public IPAddress IpAddress { get; set; } = IPAddress.None;

    /// <summary>
    /// Gets or sets the port number the peer is listening on.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this peer was last seen or heard from.
    /// Used for determining if a peer is still active.
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the statistics for this peer.
    /// Tracks performance metrics and behavior for reputation scoring.
    /// </summary>
    public PeerStatistics Stats { get; set; } = new();

    /// <summary>
    /// Gets the endpoint (IP:Port) for this peer.
    /// </summary>
    public string Endpoint => $"{IpAddress}:{Port}";

    /// <summary>
    /// Gets a value indicating whether this peer is considered stale.
    /// A peer is stale if it hasn't been seen in over 5 minutes.
    /// </summary>
    public bool IsStale => DateTime.UtcNow - LastSeen > TimeSpan.FromMinutes(5);

    /// <summary>
    /// Validates the peer information.
    /// </summary>
    /// <returns>True if all required fields are valid; otherwise, false.</returns>
    public bool Validate()
    {
        if (PeerId.Length != 20)
            return false;

        if (IpAddress.Equals(IPAddress.None) || IpAddress.Equals(IPAddress.Any))
            return false;

        if (Port < 1 || Port > 65535)
            return false;

        return true;
    }

    /// <summary>
    /// Updates the LastSeen timestamp to the current time.
    /// </summary>
    public void UpdateLastSeen()
    {
        LastSeen = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns a string representation of this peer.
    /// </summary>
    /// <returns>A string in the format "PeerId@IP:Port".</returns>
    public override string ToString()
    {
        var peerIdHex = PeerId.Length >= 4 
            ? BitConverter.ToString(PeerId[..4]).Replace("-", "").ToLower()
            : "unknown";
        return $"{peerIdHex}...@{Endpoint}";
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current peer.
    /// Two peers are equal if they have the same PeerId.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if the peers are equal; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not PeerInfo other)
            return false;

        return PeerId.SequenceEqual(other.PeerId);
    }

    /// <summary>
    /// Returns a hash code for this peer based on the PeerId.
    /// </summary>
    /// <returns>A hash code for the current peer.</returns>
    public override int GetHashCode()
    {
        return PeerId.Length >= 4 
            ? BitConverter.ToInt32(PeerId, 0) 
            : 0;
    }
}

/// <summary>
/// Represents performance and behavior statistics for a peer.
/// </summary>
/// <remarks>
/// Used for peer reputation scoring and selection.
/// Peers with better statistics are preferred for connections.
/// High hash failure rates indicate a malicious or buggy peer.
/// </remarks>
public class PeerStatistics
{
    /// <summary>
    /// Gets or sets the total number of bytes downloaded from this peer.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes uploaded to this peer.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long BytesUploaded { get; set; }

    /// <summary>
    /// Gets or sets the number of pieces that failed hash verification from this peer.
    /// High values indicate a problematic peer.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int HashFailures { get; set; }

    /// <summary>
    /// Gets or sets the average response time for requests to this peer.
    /// Lower values indicate a more responsive peer.
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the connection to this peer was established.
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the connection to this peer was closed.
    /// </summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>
    /// Gets the total number of pieces successfully downloaded from this peer.
    /// </summary>
    public int PiecesDownloaded { get; set; }

    /// <summary>
    /// Gets the total number of pieces successfully uploaded to this peer.
    /// </summary>
    public int PiecesUploaded { get; set; }

    /// <summary>
    /// Gets the connection duration if the peer is or was connected.
    /// </summary>
    public TimeSpan? ConnectionDuration
    {
        get
        {
            if (ConnectedAt == null)
                return null;

            var endTime = DisconnectedAt ?? DateTime.UtcNow;
            return endTime - ConnectedAt.Value;
        }
    }

    /// <summary>
    /// Gets the download rate in bytes per second.
    /// Returns 0 if no connection duration is available.
    /// </summary>
    public double DownloadRate
    {
        get
        {
            var duration = ConnectionDuration;
            if (duration == null || duration.Value.TotalSeconds < 1)
                return 0;

            return BytesDownloaded / duration.Value.TotalSeconds;
        }
    }

    /// <summary>
    /// Gets the upload rate in bytes per second.
    /// Returns 0 if no connection duration is available.
    /// </summary>
    public double UploadRate
    {
        get
        {
            var duration = ConnectionDuration;
            if (duration == null || duration.Value.TotalSeconds < 1)
                return 0;

            return BytesUploaded / duration.Value.TotalSeconds;
        }
    }

    /// <summary>
    /// Gets the hash failure rate as a percentage.
    /// Returns 0 if no pieces have been downloaded.
    /// </summary>
    public double HashFailureRate
    {
        get
        {
            if (PiecesDownloaded == 0)
                return 0;

            return (double)HashFailures / (PiecesDownloaded + HashFailures) * 100;
        }
    }

    /// <summary>
    /// Gets a reputation score for this peer (0-100).
    /// Higher scores indicate better peers.
    /// </summary>
    /// <remarks>
    /// Score calculation:
    /// - Start with 100 points
    /// - Subtract 10 points per 1% hash failure rate
    /// - Add points for good download/upload rates
    /// - Subtract points for slow response times
    /// </remarks>
    public double ReputationScore
    {
        get
        {
            double score = 100;

            // Penalize hash failures heavily
            score -= HashFailureRate * 10;

            // Reward good response times (under 1 second)
            if (AverageResponseTime.TotalSeconds < 1)
                score += 10;
            else if (AverageResponseTime.TotalSeconds > 5)
                score -= 20;

            // Reward active uploading (tit-for-tat)
            if (PiecesUploaded > 0)
                score += Math.Min(10, PiecesUploaded / 10.0);

            // Ensure score is between 0 and 100
            return Math.Max(0, Math.Min(100, score));
        }
    }

    /// <summary>
    /// Records a successful piece download from this peer.
    /// </summary>
    /// <param name="bytes">The number of bytes in the piece.</param>
    public void RecordDownload(int bytes)
    {
        BytesDownloaded += bytes;
        PiecesDownloaded++;
    }

    /// <summary>
    /// Records a successful piece upload to this peer.
    /// </summary>
    /// <param name="bytes">The number of bytes in the piece.</param>
    public void RecordUpload(int bytes)
    {
        BytesUploaded += bytes;
        PiecesUploaded++;
    }

    /// <summary>
    /// Records a hash verification failure for a piece from this peer.
    /// </summary>
    public void RecordHashFailure()
    {
        HashFailures++;
    }

    /// <summary>
    /// Updates the average response time with a new measurement.
    /// Uses exponential moving average for smoothing.
    /// </summary>
    /// <param name="responseTime">The new response time measurement.</param>
    public void UpdateResponseTime(TimeSpan responseTime)
    {
        if (AverageResponseTime == TimeSpan.Zero)
        {
            AverageResponseTime = responseTime;
        }
        else
        {
            // Exponential moving average with alpha = 0.3
            var alpha = 0.3;
            var newAverage = (alpha * responseTime.TotalMilliseconds) + 
                           ((1 - alpha) * AverageResponseTime.TotalMilliseconds);
            AverageResponseTime = TimeSpan.FromMilliseconds(newAverage);
        }
    }
}
