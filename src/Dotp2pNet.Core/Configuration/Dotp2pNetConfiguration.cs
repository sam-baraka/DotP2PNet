namespace Dotp2pNet.Core.Configuration;

/// <summary>
/// Root configuration for the Dotp2pNet application.
/// </summary>
public class Dotp2pNetConfiguration
{
    /// <summary>
    /// Network-related configuration settings.
    /// </summary>
    public NetworkConfiguration Network { get; set; } = new();

    /// <summary>
    /// Storage-related configuration settings.
    /// </summary>
    public StorageConfiguration Storage { get; set; } = new();

    /// <summary>
    /// Logging-related configuration settings.
    /// </summary>
    public LoggingConfiguration Logging { get; set; } = new();

    /// <summary>
    /// Peer connection configuration settings.
    /// </summary>
    public PeerConfiguration Peer { get; set; } = new();
}

/// <summary>
/// Network configuration settings.
/// </summary>
public class NetworkConfiguration
{
    /// <summary>
    /// Port to listen on for incoming peer connections.
    /// </summary>
    public int ListenPort { get; set; } = 6881;

    /// <summary>
    /// Maximum number of concurrent peer connections.
    /// </summary>
    public int MaxConnections { get; set; } = 50;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Enable DHT (Distributed Hash Table) for peer discovery.
    /// </summary>
    public bool EnableDht { get; set; } = true;

    /// <summary>
    /// DHT port (typically same as ListenPort).
    /// </summary>
    public int DhtPort { get; set; } = 6881;
}

/// <summary>
/// Storage configuration settings.
/// </summary>
public class StorageConfiguration
{
    /// <summary>
    /// Directory where downloaded files will be stored.
    /// </summary>
    public string DownloadDirectory { get; set; } = "./downloads";

    /// <summary>
    /// Directory for temporary piece storage during download.
    /// </summary>
    public string TempDirectory { get; set; } = "./temp";

    /// <summary>
    /// Maximum disk space to use for downloads (in bytes). 0 = unlimited.
    /// </summary>
    public long MaxDiskSpaceBytes { get; set; } = 0;

    /// <summary>
    /// Size of each piece in bytes (typically 256KB or 512KB).
    /// </summary>
    public int PieceLengthBytes { get; set; } = 262144; // 256KB
}

/// <summary>
/// Logging configuration settings.
/// </summary>
public class LoggingConfiguration
{
    /// <summary>
    /// Directory where log files will be stored.
    /// </summary>
    public string LogDirectory { get; set; } = "~/.dotp2pnet/logs";

    /// <summary>
    /// Minimum log level (Verbose, Debug, Information, Warning, Error, Fatal).
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// Enable console logging.
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// Enable file logging.
    /// </summary>
    public bool EnableFile { get; set; } = true;
}

/// <summary>
/// Peer connection configuration settings.
/// </summary>
public class PeerConfiguration
{
    /// <summary>
    /// Our peer ID prefix (will be completed with random bytes).
    /// </summary>
    public string PeerIdPrefix { get; set; } = "-DP0001-";

    /// <summary>
    /// Maximum number of simultaneous piece requests per peer.
    /// </summary>
    public int MaxRequestsPerPeer { get; set; } = 5;

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Peer inactivity timeout in seconds (2 minutes default).
    /// </summary>
    public int InactivityTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum number of connection retry attempts.
    /// </summary>
    public int MaxConnectionRetries { get; set; } = 3;

    /// <summary>
    /// Initial retry delay in milliseconds for exponential backoff.
    /// </summary>
    public int RetryInitialDelayMs { get; set; } = 1000;

    /// <summary>
    /// Enable choking algorithm (bandwidth management).
    /// </summary>
    public bool EnableChoking { get; set; } = true;

    /// <summary>
    /// Number of unchoked peers to maintain.
    /// </summary>
    public int UnchokedPeers { get; set; } = 4;
}
