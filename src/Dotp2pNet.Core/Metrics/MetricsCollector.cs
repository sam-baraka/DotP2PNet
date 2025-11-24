using System.Collections.Concurrent;

namespace Dotp2pNet.Core.Metrics;

/// <summary>
/// Collects and tracks system metrics for monitoring and observability.
/// </summary>
/// <remarks>
/// Uses sliding window calculations for rates to provide smooth, accurate metrics.
/// Thread-safe for concurrent access from multiple components.
/// </remarks>
public class MetricsCollector
{
    private readonly object _lock = new();
    private readonly TimeSpan _windowDuration;
    private readonly List<DataPoint> _downloadSamples = new();
    private readonly List<DataPoint> _uploadSamples = new();
    private readonly ConcurrentDictionary<byte[], TorrentMetrics> _torrentMetrics = new(new ByteArrayComparer());

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsCollector"/> class.
    /// </summary>
    /// <param name="windowDuration">The duration of the sliding window for rate calculations. Default is 10 seconds.</param>
    public MetricsCollector(TimeSpan? windowDuration = null)
    {
        _windowDuration = windowDuration ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    ///   /// Records bywnloaded for a specific torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    /// <param name="bytes">The number of bytes downloaded.</param>
    public void RecordDownload(byte[] infoHash, long bytes)
    {
        if (bytes <= 0)
            return;

        var now = DateTime.UtcNow;

        lock (_lock)
        {
            _downloadSamples.Add(new DataPoint(now, bytes));
            CleanOldSamples(_downloadSamples, now);
        }

        var metrics = GetOrCreateTorrentMetrics(infoHash);
        metrics.RecordDownload(bytes);
    }

    /// <summary>
    /// Records bytes uploaded for a specific torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    /// <param name="bytes">The number of bytes uploaded.</param>
    public void RecordUpload(byte[] infoHash, long bytes)
    {
        if (bytes <= 0)
            return;

        var now = DateTime.UtcNow;

        lock (_lock)
        {
            _uploadSamples.Add(new DataPoint(now, bytes));
            CleanOldSamples(_uploadSamples, now);
        }

        var metrics = GetOrCreateTorrentMetrics(infoHash);
        metrics.RecordUpload(bytes);
    }

    /// <summary>
    /// Records a piece completion for a specific torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    /// <param name="pieceIndex">The index of the completed piece.</param>
    public void RecordPieceCompleted(byte[] infoHash, int pieceIndex)
    {
        var metrics = GetOrCreateTorrentMetrics(infoHash);
        metrics.RecordPieceCompleted(pieceIndex);
    }

    /// <summary>
    /// Records a hash verification result for a specific torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    /// <param name="success">True if the hash verification succeeded; otherwise, false.</param>
    public void RecordHashVerification(byte[] infoHash, bool success)
    {
        var metrics = GetOrCreateTorrentMetrics(infoHash);
        metrics.RecordHashVerification(success);
    }

    /// <summary>
    /// Records peer connection count for a specific torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    /// <param name="activeConnections">The number of active peer connections.</param>
    /// <param name="totalPeers">The total number of known peers.</param>
    public void RecordPeerConnections(byte[] infoHash, int activeConnections, int totalPeers)
    {
        var metrics = GetOrCreateTorrentMetrics(infoHash);
        metrics.RecordPeerConnections(activeConnections, totalPeers);
    }

    /// <summary>
    /// Records DHT routing table size.
    /// </summary>
    /// <param name="size">The number of nodes in the routing table.</param>
    public void RecordDhtRoutingTableSize(int size)
    {
        lock (_lock)
        {
            DhtRoutingTableSize = size;
            LastDhtUpdate = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Gets the current download rate in bytes per second using sliding window calculation.
    /// </summary>
    /// <returns>The download rate in bytes per second.</returns>
    public long GetDownloadRate()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            CleanOldSamples(_downloadSamples, now);

            if (_downloadSamples.Count == 0)
                return 0;

            var totalBytes = _downloadSamples.Sum(s => s.Bytes);
            var oldestSample = _downloadSamples.Min(s => s.Timestamp);
            var duration = (now - oldestSample).TotalSeconds;

            if (duration < 0.1)
                return 0;

            return (long)(totalBytes / duration);
        }
    }

    /// <summary>
    /// Gets the current upload rate in bytes per second using sliding window calculation.
    /// </summary>
    /// <returns>The upload rate in bytes per second.</returns>
    public long GetUploadRate()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            CleanOldSamples(_uploadSamples, now);

            if (_uploadSamples.Count == 0)
                return 0;

            var totalBytes = _uploadSamples.Sum(s => s.Bytes);
            var oldestSample = _uploadSamples.Min(s => s.Timestamp);
            var duration = (now - oldestSample).TotalSeconds;

            if (duration < 0.1)
                return 0;

            return (long)(totalBytes / duration);
        }
    }

    /// <summary>
    /// Gets metrics for a specific torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    /// <returns>The torrent metrics, or null if not found.</returns>
    public TorrentMetrics? GetTorrentMetrics(byte[] infoHash)
    {
        return _torrentMetrics.TryGetValue(infoHash, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Gets all torrent metrics.
    /// </summary>
    /// <returns>A dictionary of all torrent metrics keyed by info hash.</returns>
    public IReadOnlyDictionary<byte[], TorrentMetrics> GetAllTorrentMetrics()
    {
        return _torrentMetrics;
    }

    /// <summary>
    /// Gets the current DHT routing table size.
    /// </summary>
    public int DhtRoutingTableSize { get; private set; }

    /// <summary>
    /// Gets the timestamp of the last DHT update.
    /// </summary>
    public DateTime LastDhtUpdate { get; private set; } = DateTime.MinValue;

    /// <summary>
    ///     /// Resettes s.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _downloadSamples.Clear();
            _uploadSamples.Clear();
            _torrentMetrics.Clear();
            DhtRoutingTableSize = 0;
            LastDhtUpdate = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Removes metrics for a specific torrent.
    /// </summary>
    /// <param name="infoHash">The info hash of the torrent.</param>
    public void RemoveTorrentMetrics(byte[] infoHash)
    {
        _torrentMetrics.TryRemove(infoHash, out _);
    }

    private TorrentMetrics GetOrCreateTorrentMetrics(byte[] infoHash)
    {
        return _torrentMetrics.GetOrAdd(infoHash, _ => new TorrentMetrics());
    }

    private void CleanOldSamples(List<DataPoint> samples, DateTime now)
    {
        var cutoff = now - _windowDuration;
        samples.RemoveAll(s => s.Timestamp < cutoff);
    }

    /// <summary>
    /// Represents a data point in the sliding window.
    /// </summary>
    private readonly struct DataPoint
    {
        public DateTime Timestamp { get; }
        public long Bytes { get; }

        public DataPoint(DateTime timestamp, long bytes)
        {
            Timestamp = timestamp;
            Bytes = bytes;
        }
    }

    /// <summary>
    /// Comparer for byte arrays to use as dictionary keys.
    /// </summary>
    private class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y)
        {
            if (x == null || y == null)
                return x == y;

            return x.SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            if (obj == null)
                return 0;

            unchecked
            {
                int hash = 17;
                foreach (byte b in obj)
                {
                    hash = hash * 31 + b;
                }
                return hash;
            }
        }
    }
}

/// <summary>
/// Tracks metrics for a specific torrent.
/// </summary>
public class TorrentMetrics
{
    private readonly object _lock = new();
    private long _totalDownloaded;
    private long _totalUploaded;
    private int _piecesCompleted;
    private int _hashVerificationSuccesses;
    private int _hashVerificationFailures;
    private int _activeConnections;
    private int _totalPeers;
    private readonly HashSet<int> _completedPieceIndices = new();

    /// <summary>
    /// Gets the total number of bytes downloaded.
    /// </summary>
    public long TotalDownloaded
    {
        get { lock (_lock) return _totalDownloaded; }
    }

    /// <summary>
    /// Gets the total number of bytes uploaded.
    /// </summary>
    public long TotalUploaded
    {
        get { lock (_lock) return _totalUploaded; }
    }

    /// <summary>
    /// Gets the number of pieces completed.
    /// </summary>
    public int PiecesCompleted
    {
        get { lock (_lock) return _piecesCompleted; }
    }

    /// <summary>
    /// Gets the number of successful hash verifications.
    /// </summary>
    public int HashVerificationSuccesses
    {
        get { lock (_lock) return _hashVerificationSuccesses; }
    }

    /// <summary>
    /// Gets the number of failed hash verifications.
    /// </summary>
    public int HashVerificationFailures
    {
        get { lock (_lock) return _hashVerificationFailures; }
    }

    /// <summary>
    /// Gets the hash verification success rate as a percentage (0-100).
    /// </summary>
    public double HashVerificationSuccessRate
    {
        get
        {
            lock (_lock)
            {
                var total = _hashVerificationSuccesses + _hashVerificationFailures;
                if (total == 0)
                    return 100.0;

                return (_hashVerificationSuccesses / (double)total) * 100.0;
            }
        }
    }

    /// <summary>
    /// Gets the number of active peer connections.
    /// </summary>
    public int ActiveConnections
    {
        get { lock (_lock) return _activeConnections; }
    }

    /// <summary>
    /// Gets the total number of known peers.
    /// </summary>
    public int TotalPeers
    {
        get { lock (_lock) return _totalPeers; }
    }

    /// <summary>
    /// Gets the set of completed piece indices.
    /// </summary>
    public IReadOnlySet<int> CompletedPieceIndices
    {
        get { lock (_lock) return new HashSet<int>(_completedPieceIndices); }
    }

    /// <summary>
    /// Records bytes downloaded.
    /// </summary>
    internal void RecordDownload(long bytes)
    {
        lock (_lock)
        {
            _totalDownloaded += bytes;
        }
    }

    /// <summary>
    /// Records bytes uploaded.
    /// </summary>
    internal void RecordUpload(long bytes)
    {
        lock (_lock)
        {
            _totalUploaded += bytes;
        }
    }

    /// <summary>
    /// Records a piece completion.
    /// </summary>
    internal void RecordPieceCompleted(int pieceIndex)
    {
        lock (_lock)
        {
            if (_completedPieceIndices.Add(pieceIndex))
            {
                _piecesCompleted++;
            }
        }
    }

    /// <summary>
    /// Records a hash verification result.
    /// </summary>
    internal void RecordHashVerification(bool success)
    {
        lock (_lock)
        {
            if (success)
                _hashVerificationSuccesses++;
            else
                _hashVerificationFailures++;
        }
    }

    /// <summary>
    /// Records peer connection counts.
    /// </summary>
    internal void RecordPeerConnections(int activeConnections, int totalPeers)
    {
        lock (_lock)
        {
            _activeConnections = activeConnections;
            _totalPeers = totalPeers;
        }
    }

    /// <summary>
    /// Returns a string representation of the torrent metrics.
    /// </summary>
    public override string ToString()
    {
        lock (_lock)
        {
            return $"Downloaded: {FormatBytes(_totalDownloaded)}, " +
                   $"Uploaded: {FormatBytes(_totalUploaded)}, " +
                   $"Pieces: {_piecesCompleted}, " +
                   $"Hash Success Rate: {HashVerificationSuccessRate:F1}%, " +
                   $"Peers: {_activeConnections}/{_totalPeers}";
        }
    }

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
}
