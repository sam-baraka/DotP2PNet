using System.Collections.Concurrent;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Metrics;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Orchestration.Engine;

/// <summary>
/// Orchestrates the entire torrent download and seeding process.
/// </summary>
/// <remarks>
/// The TorrentEngine coordinates between discovery, networking, and storage layers
/// to manage torrent downloads and uploads. It handles peer discovery, connection
/// management, piece selection, and progress tracking.
/// </remarks>
public class TorrentEngine : ITorrentEngine
{
    private readonly IConnectionManager _connectionManager;
    private readonly ITrackerClient _trackerClient;
    private readonly IDhtNode _dhtNode;
    private readonly IPieceManager _pieceManager;
    private readonly IPieceSelector _pieceSelector;
    private readonly IPeerSelector _peerSelector;
    private readonly ILogger<TorrentEngine> _logger;
    private readonly MetricsCollector _metricsCollector;

    // Track active torrents by info hash
    private readonly ConcurrentDictionary<string, TorrentContext> _activeTorrents = new();

    // Configuration
    private readonly int _listenPort;
    private readonly byte[] _peerId;
    private readonly int _maxConnections;

    /// <summary>
    /// Initializes a new instance of the <see cref="TorrentEngine"/> class.
    /// </summary>
    public TorrentEngine(
        IConnectionManager connectionManager,
        ITrackerClient trackerClient,
        IDhtNode dhtNode,
        IPieceManager pieceManager,
        IPieceSelector pieceSelector,
        IPeerSelector peerSelector,
        ILogger<TorrentEngine> logger,
        MetricsCollector? metricsCollector = null,
        int listenPort = 6881,
        int maxConnections = 50)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _trackerClient = trackerClient ?? throw new ArgumentNullException(nameof(trackerClient));
        _dhtNode = dhtNode ?? throw new ArgumentNullException(nameof(dhtNode));
        _pieceManager = pieceManager ?? throw new ArgumentNullException(nameof(pieceManager));
        _pieceSelector = pieceSelector ?? throw new ArgumentNullException(nameof(pieceSelector));
        _peerSelector = peerSelector ?? throw new ArgumentNullException(nameof(peerSelector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? new MetricsCollector();

        _listenPort = listenPort;
        _maxConnections = maxConnections;
        _peerId = GeneratePeerId();
    }

    /// <inheritdoc/>
    public async Task StartDownloadAsync(TorrentMetadata metadata, string savePath, CancellationToken ct = default)
    {
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        if (string.IsNullOrWhiteSpace(savePath))
            throw new ArgumentException("Save path cannot be empty", nameof(savePath));

        if (!metadata.Validate())
            throw new ArgumentException("Invalid torrent metadata", nameof(metadata));

        var infoHashKey = Convert.ToHexString(metadata.InfoHash);

        if (_activeTorrents.ContainsKey(infoHashKey))
        {
            _logger.LogWarning("Torrent {InfoHash} is already active", infoHashKey);
            return;
        }

        _logger.LogInformation("Starting download for torrent: {Name}", metadata.Name);

        // Create torrent context
        var context = new TorrentContext
        {
            Metadata = metadata,
            SavePath = savePath,
            Status = new TorrentStatus
            {
                InfoHash = metadata.InfoHash,
                Name = metadata.Name,
                TotalSize = metadata.TotalSize,
                State = TorrentState.Starting,
                SavePath = savePath,
                AddedAt = DateTime.UtcNow
            },
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
        };

        _activeTorrents[infoHashKey] = context;

        try
        {
            // Start the download coordination task
            context.CoordinationTask = Task.Run(async () =>
            {
                await CoordinateDownloadAsync(context, context.CancellationTokenSource.Token);
            }, context.CancellationTokenSource.Token);

            _logger.LogInformation("Download started for torrent: {Name}", metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start download for torrent: {Name}", metadata.Name);
            context.Status.State = TorrentState.Error;
            context.Status.ErrorMessage = ex.Message;
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task StartSeedingAsync(TorrentMetadata metadata, string filePath, CancellationToken ct = default)
    {
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        if (!metadata.Validate())
            throw new ArgumentException("Invalid torrent metadata", nameof(metadata));

        var infoHashKey = Convert.ToHexString(metadata.InfoHash);

        if (_activeTorrents.ContainsKey(infoHashKey))
        {
            _logger.LogWarning("Torrent {InfoHash} is already active", infoHashKey);
            return;
        }

        _logger.LogInformation("Starting seeding for torrent: {Name}", metadata.Name);

        // Create torrent context for seeding
        var context = new TorrentContext
        {
            Metadata = metadata,
            SavePath = filePath,
            Status = new TorrentStatus
            {
                InfoHash = metadata.InfoHash,
                Name = metadata.Name,
                TotalSize = metadata.TotalSize,
                State = TorrentState.Seeding,
                SavePath = filePath,
                AddedAt = DateTime.UtcNow,
                Downloaded = metadata.TotalSize,
                Progress = 100,
                Remaining = 0,
                CompletedAt = DateTime.UtcNow
            },
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
        };

        _activeTorrents[infoHashKey] = context;

        try
        {
            // Start the seeding coordination task
            context.CoordinationTask = Task.Run(async () =>
            {
                await CoordinateSeedingAsync(context, context.CancellationTokenSource.Token);
            }, context.CancellationTokenSource.Token);

            _logger.LogInformation("Seeding started for torrent: {Name}", metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start seeding for torrent: {Name}", metadata.Name);
            context.Status.State = TorrentState.Error;
            context.Status.ErrorMessage = ex.Message;
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task PauseAsync(byte[] infoHash, CancellationToken ct = default)
    {
        if (infoHash == null || infoHash.Length != 20)
            throw new ArgumentException("Invalid info hash", nameof(infoHash));

        var infoHashKey = Convert.ToHexString(infoHash);

        if (!_activeTorrents.TryGetValue(infoHashKey, out var context))
        {
            _logger.LogWarning("Torrent {InfoHash} not found", infoHashKey);
            return;
        }

        if (context.Status.State == TorrentState.Paused)
        {
            _logger.LogInformation("Torrent {InfoHash} is already paused", infoHashKey);
            return;
        }

        _logger.LogInformation("Pausing torrent: {Name}", context.Metadata.Name);

        context.PreviousState = context.Status.State;
        context.Status.State = TorrentState.Paused;
        context.IsPaused = true;

        _logger.LogInformation("Torrent paused: {Name}", context.Metadata.Name);

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task ResumeAsync(byte[] infoHash, CancellationToken ct = default)
    {
        if (infoHash == null || infoHash.Length != 20)
            throw new ArgumentException("Invalid info hash", nameof(infoHash));

        var infoHashKey = Convert.ToHexString(infoHash);

        if (!_activeTorrents.TryGetValue(infoHashKey, out var context))
        {
            _logger.LogWarning("Torrent {InfoHash} not found", infoHashKey);
            return;
        }

        if (context.Status.State != TorrentState.Paused)
        {
            _logger.LogInformation("Torrent {InfoHash} is not paused", infoHashKey);
            return;
        }

        _logger.LogInformation("Resuming torrent: {Name}", context.Metadata.Name);

        context.IsPaused = false;
        context.Status.State = context.PreviousState ?? TorrentState.Downloading;

        _logger.LogInformation("Torrent resumed: {Name}", context.Metadata.Name);

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(byte[] infoHash, CancellationToken ct = default)
    {
        if (infoHash == null || infoHash.Length != 20)
            throw new ArgumentException("Invalid info hash", nameof(infoHash));

        var infoHashKey = Convert.ToHexString(infoHash);

        if (!_activeTorrents.TryRemove(infoHashKey, out var context))
        {
            _logger.LogWarning("Torrent {InfoHash} not found", infoHashKey);
            return;
        }

        _logger.LogInformation("Stopping torrent: {Name}", context.Metadata.Name);

        try
        {
            // Cancel the coordination task
            context.CancellationTokenSource.Cancel();

            // Wait for coordination task to complete
            if (context.CoordinationTask != null)
            {
                try
                {
                    await context.CoordinationTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
            }

            // Announce stopped to trackers
            if (context.Metadata.Trackers.Count > 0)
            {
                foreach (var tracker in context.Metadata.Trackers)
                {
                    try
                    {
                        await _trackerClient.AnnounceAsync(
                            tracker,
                            context.Metadata.InfoHash,
                            _peerId,
                            _listenPort,
                            context.Status.Downloaded,
                            context.Status.Uploaded,
                            context.Status.Remaining,
                            TrackerEvent.Stopped,
                            ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to announce stopped to tracker: {Tracker}", tracker);
                    }
                }
            }

            // Disconnect from all peers for this torrent
            foreach (var connection in context.PeerConnections.Values)
            {
                try
                {
                    await _connectionManager.RemoveConnectionAsync(connection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to disconnect from peer");
                }
            }

            context.Status.State = TorrentState.Stopped;

            _logger.LogInformation("Torrent stopped: {Name}", context.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping torrent: {Name}", context.Metadata.Name);
            throw;
        }
        finally
        {
            context.CancellationTokenSource.Dispose();
        }
    }

    /// <inheritdoc/>
    public TorrentStatus? GetStatus(byte[] infoHash)
    {
        if (infoHash == null || infoHash.Length != 20)
            return null;

        var infoHashKey = Convert.ToHexString(infoHash);

        if (_activeTorrents.TryGetValue(infoHashKey, out var context))
        {
            // Update metrics
            UpdateStatusFromMetrics(context, infoHash);
            
            // Update calculated fields
            context.Status.UpdateProgress();
            context.Status.UpdateEstimatedTime();
            return context.Status;
        }

        return null;
    }

    /// <inheritdoc/>
    public List<TorrentStatus> GetAllStatuses()
    {
        var statuses = new List<TorrentStatus>();

        foreach (var context in _activeTorrents.Values)
        {
            // Update metrics
            UpdateStatusFromMetrics(context, context.Metadata.InfoHash);
            
            context.Status.UpdateProgress();
            context.Status.UpdateEstimatedTime();
            statuses.Add(context.Status);
        }

        return statuses;
    }

    /// <summary>
    /// Gets the metrics collector for accessing detailed metrics.
    /// </summary>
    /// <returns>The metrics collector instance.</returns>
    public MetricsCollector GetMetricsCollector()
    {
        return _metricsCollector;
    }

    /// <summary>
    /// Coordinates the download process for a torrent.
    /// </summary>
    private async Task CoordinateDownloadAsync(TorrentContext context, CancellationToken ct)
    {
        try
        {
            context.Status.State = TorrentState.Downloading;

            // Step 1: Discover peers from trackers
            await DiscoverPeersFromTrackersAsync(context, ct);

            // Step 2: Discover peers from DHT
            await DiscoverPeersFromDhtAsync(context, ct);

            // Step 3: Main download loop
            while (!ct.IsCancellationRequested && context.Status.Remaining > 0)
            {
                // Check if paused
                if (context.IsPaused)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                // Step 3a: Connect to peers
                await ConnectToPeersAsync(context, ct);

                // Step 3b: Request pieces from peers
                await RequestPiecesAsync(context, ct);

                // Step 3c: Update status
                UpdateDownloadStatus(context);

                // Wait a bit before next iteration
                await Task.Delay(100, ct);
            }

            // Check if download is complete
            if (context.Status.Remaining == 0)
            {
                context.Status.MarkComplete();
                _logger.LogInformation("Download complete for torrent: {Name}", context.Metadata.Name);

                // Announce completed to trackers
                await AnnounceCompletedAsync(context, ct);

                // Transition to seeding
                await CoordinateSeedingAsync(context, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download cancelled for torrent: {Name}", context.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during download coordination for torrent: {Name}", context.Metadata.Name);
            context.Status.State = TorrentState.Error;
            context.Status.ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Coordinates the seeding process for a torrent.
    /// </summary>
    private async Task CoordinateSeedingAsync(TorrentContext context, CancellationToken ct)
    {
        try
        {
            context.Status.State = TorrentState.Seeding;

            // Announce to trackers as seeder
            await AnnounceStartedAsync(context, ct);

            // Announce to DHT
            if (_dhtNode.IsRunning)
            {
                await _dhtNode.AnnouncePeerAsync(context.Metadata.InfoHash, _listenPort, ct);
            }

            // Main seeding loop - just keep connections alive and serve pieces
            while (!ct.IsCancellationRequested)
            {
                // Check if paused
                if (context.IsPaused)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                // Periodic tracker announces
                if (DateTime.UtcNow - context.LastTrackerAnnounce > TimeSpan.FromMinutes(30))
                {
                    await AnnounceToTrackersAsync(context, TrackerEvent.None, ct);
                }

                // Update status
                UpdateSeedingStatus(context);

                // Wait before next iteration
                await Task.Delay(5000, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Seeding cancelled for torrent: {Name}", context.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during seeding coordination for torrent: {Name}", context.Metadata.Name);
            context.Status.State = TorrentState.Error;
            context.Status.ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Discovers peers from configured trackers.
    /// </summary>
    private async Task DiscoverPeersFromTrackersAsync(TorrentContext context, CancellationToken ct)
    {
        if (context.Metadata.Trackers.Count == 0)
        {
            _logger.LogInformation("No trackers configured for torrent: {Name}", context.Metadata.Name);
            return;
        }

        _logger.LogInformation("Discovering peers from {Count} trackers", context.Metadata.Trackers.Count);

        foreach (var tracker in context.Metadata.Trackers)
        {
            try
            {
                var response = await _trackerClient.AnnounceAsync(
                    tracker,
                    context.Metadata.InfoHash,
                    _peerId,
                    _listenPort,
                    context.Status.Downloaded,
                    context.Status.Uploaded,
                    context.Status.Remaining,
                    TrackerEvent.Started,
                    ct);

                context.LastTrackerAnnounce = DateTime.UtcNow;

                // Add discovered peers
                foreach (var peer in response.Peers)
                {
                    var peerKey = $"{peer.IpAddress}:{peer.Port}";
                    context.AvailablePeers[peerKey] = peer;
                }

                context.Status.AvailablePeers = context.AvailablePeers.Count;
                context.Status.Seeders = response.Complete;
                context.Status.Leechers = response.Incomplete;

                _logger.LogInformation("Discovered {Count} peers from tracker: {Tracker}", response.Peers.Count, tracker);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to announce to tracker: {Tracker}", tracker);
            }
        }
    }

    /// <summary>
    /// Discovers peers from DHT.
    /// </summary>
    private async Task DiscoverPeersFromDhtAsync(TorrentContext context, CancellationToken ct)
    {
        if (!_dhtNode.IsRunning)
        {
            _logger.LogInformation("DHT is not running, skipping DHT peer discovery");
            return;
        }

        try
        {
            _logger.LogInformation("Discovering peers from DHT");

            var peers = await _dhtNode.FindPeersAsync(context.Metadata.InfoHash, ct);

            foreach (var peer in peers)
            {
                var peerKey = $"{peer.IpAddress}:{peer.Port}";
                context.AvailablePeers[peerKey] = peer;
            }

            context.Status.AvailablePeers = context.AvailablePeers.Count;

            _logger.LogInformation("Discovered {Count} peers from DHT", peers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover peers from DHT");
        }
    }

    /// <summary>
    /// Connects to available peers.
    /// </summary>
    private async Task ConnectToPeersAsync(TorrentContext context, CancellationToken ct)
    {
        // Get list of peers we're already connected to
        var connectedPeerKeys = context.PeerConnections.Keys.ToHashSet();
        var currentConnections = context.AvailablePeers.Values
            .Where(p => connectedPeerKeys.Contains($"{p.IpAddress}:{p.Port}"))
            .ToList();

        var peersToConnect = _peerSelector.SelectPeersToConnect(
            context.AvailablePeers.Values.ToList(),
            currentConnections,
            _maxConnections);

        foreach (var peer in peersToConnect)
        {
            if (ct.IsCancellationRequested)
                break;

            var peerKey = $"{peer.IpAddress}:{peer.Port}";
            
            // Skip if already connected
            if (context.PeerConnections.ContainsKey(peerKey))
                continue;

            try
            {
                var connection = await _connectionManager.ConnectToPeerAsync(
                    peer.IpAddress.ToString(),
                    peer.Port,
                    ct);

                context.PeerConnections[peerKey] = connection;
                context.Status.ConnectedPeers = context.PeerConnections.Count;

                _logger.LogDebug("Connected to peer: {Peer}", peer);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to connect to peer: {Peer}", peer);
            }
        }
    }

    /// <summary>
    /// Requests pieces from connected peers.
    /// </summary>
    private async Task RequestPiecesAsync(TorrentContext context, CancellationToken ct)
    {
        // This is a simplified implementation
        // In a real implementation, this would:
        // 1. Get peer bitfields
        // 2. Use piece selector to choose next piece
        // 3. Send request messages to peers
        // 4. Handle piece responses
        // 5. Verify and store pieces

        // For now, just log that we would be requesting pieces
        if (context.PeerConnections.Count > 0)
        {
            _logger.LogDebug("Would request pieces from {Count} peers", context.PeerConnections.Count);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Updates download status metrics.
    /// </summary>
    private void UpdateDownloadStatus(TorrentContext context)
    {
        // Update progress from piece manager
        context.Status.Downloaded = _pieceManager.CompletedPieces * _pieceManager.PieceLength;
        context.Status.Remaining = context.Status.TotalSize - context.Status.Downloaded;
        context.Status.UpdateProgress();

        // Record download metrics
        var elapsed = DateTime.UtcNow - context.LastStatusUpdate;
        if (elapsed.TotalSeconds >= 1)
        {
            var bytesDownloaded = context.Status.Downloaded - context.LastDownloaded;
            if (bytesDownloaded > 0)
            {
                _metricsCollector.RecordDownload(context.Metadata.InfoHash, bytesDownloaded);
            }

            context.LastDownloaded = context.Status.Downloaded;
            context.LastStatusUpdate = DateTime.UtcNow;
        }

        // Get download rate from metrics collector (uses sliding window)
        context.Status.DownloadRate = _metricsCollector.GetDownloadRate();

        // Record peer connections
        _metricsCollector.RecordPeerConnections(
            context.Metadata.InfoHash,
            context.PeerConnections.Count,
            context.AvailablePeers.Count);

        // Record DHT routing table size
        if (_dhtNode.IsRunning)
        {
            _metricsCollector.RecordDhtRoutingTableSize(_dhtNode.GetRoutingTableSize());
        }

        context.Status.UpdateEstimatedTime();
    }

    /// <summary>
    /// Updates seeding status metrics.
    /// </summary>
    private void UpdateSeedingStatus(TorrentContext context)
    {
        // Record upload metrics
        var elapsed = DateTime.UtcNow - context.LastStatusUpdate;
        if (elapsed.TotalSeconds >= 1)
        {
            var bytesUploaded = context.Status.Uploaded - context.LastUploaded;
            if (bytesUploaded > 0)
            {
                _metricsCollector.RecordUpload(context.Metadata.InfoHash, bytesUploaded);
            }

            context.LastUploaded = context.Status.Uploaded;
            context.LastStatusUpdate = DateTime.UtcNow;
        }

        // Get upload rate from metrics collector (uses sliding window)
        context.Status.UploadRate = _metricsCollector.GetUploadRate();

        // Record peer connections
        _metricsCollector.RecordPeerConnections(
            context.Metadata.InfoHash,
            context.PeerConnections.Count,
            context.AvailablePeers.Count);

        // Record DHT routing table size
        if (_dhtNode.IsRunning)
        {
            _metricsCollector.RecordDhtRoutingTableSize(_dhtNode.GetRoutingTableSize());
        }
    }

    /// <summary>
    /// Announces started event to all trackers.
    /// </summary>
    private async Task AnnounceStartedAsync(TorrentContext context, CancellationToken ct)
    {
        await AnnounceToTrackersAsync(context, TrackerEvent.Started, ct);
    }

    /// <summary>
    /// Announces completed event to all trackers.
    /// </summary>
    private async Task AnnounceCompletedAsync(TorrentContext context, CancellationToken ct)
    {
        await AnnounceToTrackersAsync(context, TrackerEvent.Completed, ct);
    }

    /// <summary>
    /// Announces to all trackers with the specified event.
    /// </summary>
    private async Task AnnounceToTrackersAsync(TorrentContext context, TrackerEvent eventType, CancellationToken ct)
    {
        foreach (var tracker in context.Metadata.Trackers)
        {
            try
            {
                await _trackerClient.AnnounceAsync(
                    tracker,
                    context.Metadata.InfoHash,
                    _peerId,
                    _listenPort,
                    context.Status.Downloaded,
                    context.Status.Uploaded,
                    context.Status.Remaining,
                    eventType,
                    ct);

                context.LastTrackerAnnounce = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to announce to tracker: {Tracker}", tracker);
            }
        }
    }

    /// <summary>
    /// Generates a unique peer ID for this client.
    /// </summary>
    private static byte[] GeneratePeerId()
    {
        var peerId = new byte[20];
        
        // Use client identifier: -DP0100- (Dotp2pNet 01.00)
        var prefix = "-DP0100-"u8.ToArray();
        Array.Copy(prefix, peerId, prefix.Length);

        // Fill rest with random bytes
        var random = new Random();
        random.NextBytes(peerId.AsSpan(prefix.Length));

        return peerId;
    }

    /// <summary>
    /// Updates torrent status from metrics collector.
    /// </summary>
    private void UpdateStatusFromMetrics(TorrentContext context, byte[] infoHash)
    {
        var metrics = _metricsCollector.GetTorrentMetrics(infoHash);
        if (metrics != null)
        {
            // Update rates from metrics collector (uses sliding window calculation)
            context.Status.DownloadRate = _metricsCollector.GetDownloadRate();
            context.Status.UploadRate = _metricsCollector.GetUploadRate();
        }
    }

    /// <summary>
    /// Stores a piece and records metrics for hash verification and completion.
    /// </summary>
    /// <param name="context">The torrent context.</param>
    /// <param name="pieceIndex">The index of the piece.</param>
    /// <param name="data">The piece data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the piece was stored successfully; otherwise, false.</returns>
    private async Task<bool> StorePieceWithMetricsAsync(
        TorrentContext context,
        int pieceIndex,
        byte[] data,
        CancellationToken ct)
    {
        var success = await _pieceManager.StorePieceAsync(pieceIndex, data, ct);

        // Record hash verification result
        _metricsCollector.RecordHashVerification(context.Metadata.InfoHash, success);

        if (success)
        {
            // Record piece completion
            _metricsCollector.RecordPieceCompleted(context.Metadata.InfoHash, pieceIndex);

            _logger.LogInformation(
                "Piece {PieceIndex} completed for torrent {Name} (hash verified)",
                pieceIndex,
                context.Metadata.Name);
        }
        else
        {
            _logger.LogWarning(
                "Piece {PieceIndex} failed hash verification for torrent {Name}",
                pieceIndex,
                context.Metadata.Name);
        }

        return success;
    }

    /// <summary>
    /// Internal context for tracking a torrent's state.
    /// </summary>
    private class TorrentContext
    {
        public required TorrentMetadata Metadata { get; init; }
        public required string SavePath { get; init; }
        public required TorrentStatus Status { get; init; }
        public required CancellationTokenSource CancellationTokenSource { get; init; }
        public Task? CoordinationTask { get; set; }

        public ConcurrentDictionary<string, PeerInfo> AvailablePeers { get; } = new();
        public ConcurrentDictionary<string, IPeerConnection> PeerConnections { get; } = new();

        public DateTime LastTrackerAnnounce { get; set; } = DateTime.MinValue;
        public DateTime LastStatusUpdate { get; set; } = DateTime.UtcNow;
        public long LastDownloaded { get; set; }
        public long LastUploaded { get; set; }

        public bool IsPaused { get; set; }
        public TorrentState? PreviousState { get; set; }
    }
}
