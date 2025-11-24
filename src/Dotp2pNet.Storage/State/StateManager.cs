using System.Text.Json;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;
using DownloadState = Dotp2pNet.Core.Interfaces.DownloadState;

namespace Dotp2pNet.Storage.State;

/// <summary>
/// Manages persistence of download state to disk for resume capability.
/// </summary>
/// <remarks>
/// The StateManager saves download state to JSON files in the user's data directory.
/// Each torrent has its own state file named by its info hash. This enables:
/// - Resuming incomplete downloads after application restart
/// - Tracking download progress across sessions
/// - Recovering from crashes without losing progress
/// 
/// State files are stored in: ~/.dotp2pnet/state/ (Unix) or %APPDATA%\dotp2pnet\state\ (Windows)
/// </remarks>
public class StateManager : IStateManager
{
    private readonly ILogger<StateManager> _logger;
    private readonly string _stateDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the StateManager class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="stateDirectory">Optional custom state directory. If null, uses default user data directory.</param>
    public StateManager(ILogger<StateManager> logger, string? stateDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Determine state directory
        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            var userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _stateDirectory = Path.Combine(userDataPath, "dotp2pnet", "state");
        }
        else
        {
            _stateDirectory = stateDirectory;
        }

        // Ensure state directory exists
        Directory.CreateDirectory(_stateDirectory);

        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _logger.LogInformation("StateManager initialized with directory: {StateDirectory}", _stateDirectory);
    }

    public async Task SaveStateAsync(
        byte[] infoHash,
        TorrentMetadata metadata,
        IBitfield bitfield,
        string downloadDirectory,
        long bytesDownloaded,
        long bytesUploaded,
        CancellationToken ct = default)
    {
        if (infoHash == null || infoHash.Length != 20)
        {
            throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));
        }

        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (bitfield == null)
        {
            throw new ArgumentNullException(nameof(bitfield));
        }

        if (string.IsNullOrWhiteSpace(downloadDirectory))
        {
            throw new ArgumentException("Download directory cannot be empty", nameof(downloadDirectory));
        }

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Create download state object
            var state = new DownloadState
            {
                Metadata = metadata,
                BitfieldBytes = bitfield.ToBytes(),
                PieceCount = bitfield.Length,
                DownloadDirectory = downloadDirectory,
                BytesDownloaded = bytesDownloaded,
                BytesUploaded = bytesUploaded,
                LastActive = DateTime.UtcNow,
                State = TorrentState.Stopped // Save as stopped since we're persisting
            };

            // Get state file path
            var stateFilePath = GetStateFilePath(infoHash);

            // Serialize to JSON
            var json = JsonSerializer.Serialize(state, _jsonOptions);

            // Write to file atomically (write to temp file, then rename)
            var tempFilePath = stateFilePath + ".tmp";
            await File.WriteAllTextAsync(tempFilePath, json, ct).ConfigureAwait(false);

            // Atomic rename (overwrites existing file)
            File.Move(tempFilePath, stateFilePath, overwrite: true);

            _logger.LogInformation(
                "Saved state for torrent {InfoHash}: {PieceCount} pieces, {BytesDownloaded} bytes downloaded",
                Convert.ToHexString(infoHash),
                state.PieceCount,
                bytesDownloaded);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to save state for torrent {InfoHash}",
                Convert.ToHexString(infoHash));
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DownloadState?> LoadStateAsync(
        byte[] infoHash,
        CancellationToken ct = default)
    {
        if (infoHash == null || infoHash.Length != 20)
        {
            throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));
        }

        var stateFilePath = GetStateFilePath(infoHash);

        if (!File.Exists(stateFilePath))
        {
            _logger.LogDebug(
                "No state file found for torrent {InfoHash}",
                Convert.ToHexString(infoHash));
            return null;
        }

        try
        {
            // Read JSON from file
            var json = await File.ReadAllTextAsync(stateFilePath, ct).ConfigureAwait(false);

            // Deserialize
            var state = JsonSerializer.Deserialize<DownloadState>(json, _jsonOptions);

            if (state == null)
            {
                _logger.LogWarning(
                    "Failed to deserialize state file for torrent {InfoHash}",
                    Convert.ToHexString(infoHash));
                return null;
            }

            _logger.LogInformation(
                "Loaded state for torrent {InfoHash}: {PieceCount} pieces, {BytesDownloaded} bytes downloaded",
                Convert.ToHexString(infoHash),
                state.PieceCount,
                state.BytesDownloaded);

            return state;
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse state file for torrent {InfoHash}",
                Convert.ToHexString(infoHash));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load state for torrent {InfoHash}",
                Convert.ToHexString(infoHash));
            throw;
        }
    }

    public async Task DeleteStateAsync(
        byte[] infoHash,
        CancellationToken ct = default)
    {
        if (infoHash == null || infoHash.Length != 20)
        {
            throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));
        }

        var stateFilePath = GetStateFilePath(infoHash);

        if (!File.Exists(stateFilePath))
        {
            _logger.LogDebug(
                "No state file to delete for torrent {InfoHash}",
                Convert.ToHexString(infoHash));
            return;
        }

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            File.Delete(stateFilePath);

            _logger.LogInformation(
                "Deleted state file for torrent {InfoHash}",
                Convert.ToHexString(infoHash));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete state file for torrent {InfoHash}",
                Convert.ToHexString(infoHash));
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public bool StateExists(byte[] infoHash)
    {
        if (infoHash == null || infoHash.Length != 20)
        {
            throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));
        }

        var stateFilePath = GetStateFilePath(infoHash);
        return File.Exists(stateFilePath);
    }

    public async Task<List<byte[]>> GetAllSavedTorrentsAsync(CancellationToken ct = default)
    {
        var infoHashes = new List<byte[]>();

        try
        {
            // Get all .json files in state directory
            var stateFiles = Directory.GetFiles(_stateDirectory, "*.json");

            foreach (var filePath in stateFiles)
            {
                ct.ThrowIfCancellationRequested();

                // Extract info hash from filename
                var fileName = Path.GetFileNameWithoutExtension(filePath);

                try
                {
                    // Convert hex string back to bytes
                    var infoHash = Convert.FromHexString(fileName);

                    if (infoHash.Length == 20)
                    {
                        infoHashes.Add(infoHash);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Skipping state file with invalid info hash length: {FileName}",
                            fileName);
                    }
                }
                catch (FormatException)
                {
                    _logger.LogWarning(
                        "Skipping state file with invalid hex filename: {FileName}",
                        fileName);
                }
            }

            _logger.LogInformation(
                "Found {Count} saved torrent(s) in state directory",
                infoHashes.Count);

            return infoHashes;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to enumerate saved torrents in {StateDirectory}",
                _stateDirectory);
            throw;
        }
    }

    /// <summary>
    /// Gets the file path for a torrent's state file.
    /// </summary>
    /// <param name="infoHash">The torrent's info hash.</param>
    /// <returns>The full path to the state file.</returns>
    private string GetStateFilePath(byte[] infoHash)
    {
        // Use hex-encoded info hash as filename
        var fileName = Convert.ToHexString(infoHash) + ".json";
        return Path.Combine(_stateDirectory, fileName);
    }
}
