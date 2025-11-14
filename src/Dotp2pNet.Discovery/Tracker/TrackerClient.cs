using System.Net;
using System.Text;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Discovery.Tracker;

/// <summary>
/// Implements HTTP tracker communication for centralized peer discovery.
/// </summary>
/// <remarks>
/// The tracker protocol works as follows:
/// 1. Client sends HTTP GET request to tracker with announce parameters
/// 2. Tracker responds with bencoded dictionary containing peer list
/// 3. Client parses response and extracts peer information
/// 4. Client periodically re-announces to maintain presence in swarm
/// 
/// Key concepts:
/// - Announce: Notify tracker of our presence and get peer list
/// - Interval: How often to re-announce (typically 30 minutes)
/// - Events: started, completed, stopped (lifecycle notifications)
/// - Compact format: Peers encoded as 6-byte binary (4 bytes IP + 2 bytes port)
/// </remarks>
public class TrackerClient : ITrackerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TrackerClient> _logger;
    private readonly Dictionary<string, DateTime> _lastAnnounce = new();
    private readonly Dictionary<string, int> _announceIntervals = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TrackerClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="logger">The logger instance.</param>
    public TrackerClient(HttpClient httpClient, ILogger<TrackerClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Set reasonable timeout for tracker requests
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc/>
    public async Task<TrackerResponse> AnnounceAsync(
        string trackerUrl,
        byte[] infoHash,
        byte[] peerId,
        int port,
        long downloaded,
        long uploaded,
        long left,
        TrackerEvent eventType,
        CancellationToken ct = default)
    {
        ValidateParameters(trackerUrl, infoHash, peerId, port);

        // Check if we should respect announce interval
        if (ShouldThrottleAnnounce(trackerUrl, eventType))
        {
            _logger.LogWarning(
                "Announce to {TrackerUrl} throttled due to interval restriction",
                trackerUrl);
            throw new InvalidOperationException(
                $"Must wait before announcing again. Interval: {_announceIntervals[trackerUrl]} seconds");
        }

        try
        {
            // Build announce URL with query parameters
            string announceUrl = BuildAnnounceUrl(
                trackerUrl,
                infoHash,
                peerId,
                port,
                downloaded,
                uploaded,
                left,
                eventType);

            _logger.LogInformation(
                "Announcing to tracker {TrackerUrl} with event {Event}",
                trackerUrl,
                eventType);

            // Send HTTP GET request to tracker
            var response = await _httpClient.GetAsync(announceUrl, ct);
            response.EnsureSuccessStatusCode();

            // Read response body
            byte[] responseData = await response.Content.ReadAsByteArrayAsync(ct);

            // Parse bencoded response
            var trackerResponse = ParseTrackerResponse(responseData);

            // Update announce tracking
            _lastAnnounce[trackerUrl] = DateTime.UtcNow;
            _announceIntervals[trackerUrl] = trackerResponse.Interval;

            _logger.LogInformation(
                "Received {PeerCount} peers from tracker {TrackerUrl}. " +
                "Seeders: {Seeders}, Leechers: {Leechers}, Interval: {Interval}s",
                trackerResponse.Peers.Count,
                trackerUrl,
                trackerResponse.Complete,
                trackerResponse.Incomplete,
                trackerResponse.Interval);

            return trackerResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "HTTP error while announcing to tracker {TrackerUrl}",
                trackerUrl);
            throw new InvalidOperationException($"Failed to announce to tracker: {ex.Message}", ex);
        }
        catch (FormatException ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse tracker response from {TrackerUrl}",
                trackerUrl);
            throw new InvalidOperationException($"Invalid tracker response format: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<List<PeerInfo>> GetPeersAsync(
        string trackerUrl,
        byte[] infoHash,
        byte[] peerId,
        int port,
        CancellationToken ct = default)
    {
        var response = await AnnounceAsync(
            trackerUrl,
            infoHash,
            peerId,
            port,
            downloaded: 0,
            uploaded: 0,
            left: 0,
            TrackerEvent.None,
            ct);

        return response.Peers;
    }

    private static void ValidateParameters(string trackerUrl, byte[] infoHash, byte[] peerId, int port)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            throw new ArgumentException("Tracker URL cannot be null or empty", nameof(trackerUrl));
        }

        if (!Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri) || 
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException("Tracker URL must be a valid HTTP or HTTPS URL", nameof(trackerUrl));
        }

        if (infoHash == null || infoHash.Length != 20)
        {
            throw new ArgumentException("Info hash must be exactly 20 bytes", nameof(infoHash));
        }

        if (peerId == null || peerId.Length != 20)
        {
            throw new ArgumentException("Peer ID must be exactly 20 bytes", nameof(peerId));
        }

        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }
    }

    private bool ShouldThrottleAnnounce(string trackerUrl, TrackerEvent eventType)
    {
        // Don't throttle lifecycle events (started, completed, stopped)
        if (eventType != TrackerEvent.None)
        {
            return false;
        }

        // Check if we have announced before and need to respect interval
        if (_lastAnnounce.TryGetValue(trackerUrl, out var lastTime) &&
            _announceIntervals.TryGetValue(trackerUrl, out var interval))
        {
            var elapsed = DateTime.UtcNow - lastTime;
            return elapsed.TotalSeconds < interval;
        }

        return false;
    }

    private static string BuildAnnounceUrl(
        string trackerUrl,
        byte[] infoHash,
        byte[] peerId,
        int port,
        long downloaded,
        long uploaded,
        long left,
        TrackerEvent eventType)
    {
        var builder = new StringBuilder(trackerUrl);

        // Add query separator
        builder.Append(trackerUrl.Contains('?') ? '&' : '?');

        // Required parameters
        builder.Append("info_hash=").Append(UrlEncodeBytes(infoHash));
        builder.Append("&peer_id=").Append(UrlEncodeBytes(peerId));
        builder.Append("&port=").Append(port);
        builder.Append("&uploaded=").Append(uploaded);
        builder.Append("&downloaded=").Append(downloaded);
        builder.Append("&left=").Append(left);
        builder.Append("&compact=1"); // Request compact peer format

        // Optional event parameter
        if (eventType != TrackerEvent.None)
        {
            builder.Append("&event=").Append(eventType.ToString().ToLowerInvariant());
        }

        return builder.ToString();
    }

    /// <summary>
    /// URL-encodes a byte array according to BitTorrent specification.
    /// </summary>
    /// <remarks>
    /// BitTorrent uses a specific URL encoding where:
    /// - Alphanumeric characters, '.', '-', '_', '~' are not encoded
    /// - All other bytes are encoded as %XX where XX is the hex value
    /// </remarks>
    private static string UrlEncodeBytes(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 3);

        foreach (byte b in bytes)
        {
            // Check if byte should be encoded
            if ((b >= 'a' && b <= 'z') ||
                (b >= 'A' && b <= 'Z') ||
                (b >= '0' && b <= '9') ||
                b == '.' || b == '-' || b == '_' || b == '~')
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    private TrackerResponse ParseTrackerResponse(byte[] responseData)
    {
        // Parse bencoded response
        var dict = BencodeParser.ParseDictionary(responseData);

        // Check for failure reason
        string? failureReason = BencodeParser.GetString(dict, "failure reason");
        if (failureReason != null)
        {
            throw new InvalidOperationException($"Tracker returned failure: {failureReason}");
        }

        var response = new TrackerResponse
        {
            Interval = (int)(BencodeParser.GetInteger(dict, "interval") ?? 1800), // Default 30 minutes
            MinInterval = (int?)BencodeParser.GetInteger(dict, "min interval"),
            TrackerId = BencodeParser.GetString(dict, "tracker id"),
            Complete = (int)(BencodeParser.GetInteger(dict, "complete") ?? 0),
            Incomplete = (int)(BencodeParser.GetInteger(dict, "incomplete") ?? 0),
            WarningMessage = BencodeParser.GetString(dict, "warning message")
        };

        // Parse peers
        response.Peers = ParsePeers(dict);

        if (response.WarningMessage != null)
        {
            _logger.LogWarning(
                "Tracker warning: {Warning}",
                response.WarningMessage);
        }

        return response;
    }

    private List<PeerInfo> ParsePeers(Dictionary<string, object> dict)
    {
        var peers = new List<PeerInfo>();

        // Try compact format first (binary)
        byte[]? compactPeers = BencodeParser.GetBytes(dict, "peers");
        if (compactPeers != null)
        {
            peers.AddRange(ParseCompactPeers(compactPeers));
            return peers;
        }

        // Try dictionary format (list of dictionaries)
        var peerList = BencodeParser.GetList(dict, "peers");
        if (peerList != null)
        {
            peers.AddRange(ParseDictionaryPeers(peerList));
            return peers;
        }

        _logger.LogWarning("No peers found in tracker response");
        return peers;
    }

    /// <summary>
    /// Parses peers in compact binary format.
    /// </summary>
    /// <remarks>
    /// Compact format: Each peer is 6 bytes (4 bytes IP + 2 bytes port)
    /// Example: [192.168.1.50:6881] = [C0 A8 01 32 1A E1]
    /// </remarks>
    private List<PeerInfo> ParseCompactPeers(byte[] compactPeers)
    {
        var peers = new List<PeerInfo>();

        if (compactPeers.Length % 6 != 0)
        {
            _logger.LogWarning(
                "Invalid compact peers length: {Length} (expected multiple of 6)",
                compactPeers.Length);
            return peers;
        }

        for (int i = 0; i < compactPeers.Length; i += 6)
        {
            try
            {
                // Extract IP address (4 bytes)
                byte[] ipBytes = new byte[4];
                Array.Copy(compactPeers, i, ipBytes, 0, 4);
                var ipAddress = new IPAddress(ipBytes);

                // Extract port (2 bytes, big-endian)
                int port = (compactPeers[i + 4] << 8) | compactPeers[i + 5];

                var peerInfo = new PeerInfo
                {
                    IpAddress = ipAddress,
                    Port = port,
                    PeerId = Array.Empty<byte>(), // Not provided in compact format
                    LastSeen = DateTime.UtcNow
                };

                peers.Add(peerInfo);

                _logger.LogDebug(
                    "Parsed compact peer: {Endpoint}",
                    peerInfo.Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse compact peer at offset {Offset}",
                    i);
            }
        }

        return peers;
    }

    /// <summary>
    /// Parses peers in dictionary format.
    /// </summary>
    /// <remarks>
    /// Dictionary format: List of dictionaries with keys "peer id", "ip", "port"
    /// Example: [{"peer id": "...", "ip": "192.168.1.50", "port": 6881}]
    /// </remarks>
    private List<PeerInfo> ParseDictionaryPeers(List<object> peerList)
    {
        var peers = new List<PeerInfo>();

        foreach (var peerObj in peerList)
        {
            if (peerObj is not Dictionary<string, object> peerDict)
            {
                continue;
            }

            try
            {
                string? ipStr = BencodeParser.GetString(peerDict, "ip");
                long? portLong = BencodeParser.GetInteger(peerDict, "port");
                byte[]? peerId = BencodeParser.GetBytes(peerDict, "peer id");

                if (ipStr == null || portLong == null)
                {
                    _logger.LogWarning("Peer dictionary missing required fields");
                    continue;
                }

                if (!IPAddress.TryParse(ipStr, out var ipAddress))
                {
                    _logger.LogWarning("Invalid IP address in peer dictionary: {IP}", ipStr);
                    continue;
                }

                var peerInfo = new PeerInfo
                {
                    IpAddress = ipAddress,
                    Port = (int)portLong.Value,
                    PeerId = peerId ?? Array.Empty<byte>(),
                    LastSeen = DateTime.UtcNow
                };

                peers.Add(peerInfo);

                _logger.LogDebug(
                    "Parsed dictionary peer: {Endpoint}",
                    peerInfo.Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse dictionary peer");
            }
        }

        return peers;
    }
}
