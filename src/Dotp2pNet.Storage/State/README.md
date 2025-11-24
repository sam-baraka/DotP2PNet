# Download State Persistence

This directory contains the state management system for persisting download progress to disk, enabling resume capability after application restart.

## Overview

The state persistence system saves the current state of torrent downloads to JSON files in the user's data directory. This allows users to:

- Resume incomplete downloads after application restart
- Recover from crashes without losing download progress
- Track download statistics across sessions
- Maintain download state even when the application is closed

## Components

### StateManager

The `StateManager` class implements the `IStateManager` interface and provides methods for saving, loading, and managing download state files.

**Key Features:**
- Thread-safe state file operations using `SemaphoreSlim`
- Atomic file writes (write to temp file, then rename)
- JSON serialization with human-readable formatting
- Automatic state directory creation
- Validation of loaded state

**State Directory Locations:**
- **Unix/Linux/macOS**: `~/.dotp2pnet/state/`
- **Windows**: `%APPDATA%\dotp2pnet\state\`

### DownloadState

The `DownloadState` class (defined in `Core.Interfaces`) represents the persisted state of a torrent download.

**Stored Information:**
- Torrent metadata (name, size, piece hashes, etc.)
- Bitfield (which pieces have been downloaded)
- Download directory path
- Bytes downloaded and uploaded
- Last active timestamp
- Torrent state (stopped, downloading, seeding, etc.)

## Usage

### Saving State

```csharp
var stateManager = new StateManager(logger);

await stateManager.SaveStateAsync(
    infoHash: torrentInfoHash,
    metadata: torrentMetadata,
    bitfield: currentBitfield,
    downloadDirectory: "/path/to/downloads",
    bytesDownloaded: 1024000,
    bytesUploaded: 512000);
```

### Loading State

```csharp
var stateManager = new StateManager(logger);

var state = await stateManager.LoadStateAsync(torrentInfoHash);

if (state != null)
{
    // Restore download from saved state
    var bitfield = new Bitfield(state.BitfieldBytes, state.PieceCount);
    var metadata = state.Metadata;
    var downloadDirectory = state.DownloadDirectory;
    
    // Resume download...
}
```

### Checking if State Exists

```csharp
if (stateManager.StateExists(torrentInfoHash))
{
    // State file exists, can resume
}
```

### Deleting State

```csharp
// Delete state when download is complete or removed
await stateManager.DeleteStateAsync(torrentInfoHash);
```

### Getting All Saved Torrents

```csharp
var savedTorrents = await stateManager.GetAllSavedTorrentsAsync();

foreach (var infoHash in savedTorrents)
{
    var state = await stateManager.LoadStateAsync(infoHash);
    // Process each saved torrent...
}
```

## State File Format

State files are stored as JSON with the following structure:

```json
{
  "metadata": {
    "infoHash": "...",
    "name": "ubuntu-22.04.iso",
    "totalSize": 3758096384,
    "pieceSize": 262144,
    "pieceHashes": [...],
    "files": [...],
    "trackers": [...]
  },
  "bitfieldBytes": "...",
  "pieceCount": 14336,
  "downloadDirectory": "/home/user/downloads",
  "bytesDownloaded": 1879048192,
  "bytesUploaded": 524288000,
  "lastActive": "2025-11-24T10:30:00Z",
  "addedAt": "2025-11-24T09:00:00Z",
  "state": "Stopped"
}
```

**File Naming:**
- State files are named using the hex-encoded info hash: `{INFO_HASH}.json`
- Example: `3B6A27BCCEB6A42D62A3A8D02A6F0D73653215771.json`

## Implementation Details

### Thread Safety

The `StateManager` uses a `SemaphoreSlim` to ensure thread-safe file operations. Multiple threads can safely call save/load methods concurrently.

### Atomic Writes

To prevent corruption from crashes during writes, the state manager:
1. Writes to a temporary file (`{INFO_HASH}.json.tmp`)
2. Atomically renames the temp file to the final name (overwrites existing)

This ensures that state files are never partially written.

### Error Handling

- **Invalid info hash**: Throws `ArgumentException`
- **Null parameters**: Throws `ArgumentNullException`
- **File I/O errors**: Logged and re-thrown
- **JSON parsing errors**: Logged and returns `null` (graceful degradation)
- **Invalid state**: Logged and returns `null`

### Validation

Both saving and loading perform validation:
- Info hash must be exactly 20 bytes
- Metadata must be valid (proper sizes, hashes, etc.)
- Bitfield bytes must not be empty
- Piece count must be positive
- Download directory must not be empty

## Integration with TorrentEngine

The `TorrentEngine` should integrate state persistence as follows:

### On Download Start

```csharp
// Check if state exists for resume
if (stateManager.StateExists(infoHash))
{
    var state = await stateManager.LoadStateAsync(infoHash);
    if (state != null)
    {
        // Resume from saved state
        var bitfield = new Bitfield(state.BitfieldBytes, state.PieceCount);
        // Continue download from where we left off...
    }
}
```

### Periodic State Saving

```csharp
// Save state periodically (e.g., every 30 seconds)
await stateManager.SaveStateAsync(
    infoHash,
    metadata,
    pieceManager.GetBitfield(),
    downloadDirectory,
    bytesDownloaded,
    bytesUploaded);
```

### On Shutdown

```csharp
// Save state for all active torrents before shutdown
foreach (var torrent in activeTorrents)
{
    await stateManager.SaveStateAsync(
        torrent.InfoHash,
        torrent.Metadata,
        torrent.Bitfield,
        torrent.DownloadDirectory,
        torrent.BytesDownloaded,
        torrent.BytesUploaded);
}
```

### On Completion

```csharp
// Optionally delete state when download completes
if (isComplete)
{
    await stateManager.DeleteStateAsync(infoHash);
}
```

## Testing

The `StateManagerTests` class provides comprehensive unit tests covering:

- Saving and loading state
- Overwriting existing state
- Deleting state files
- Handling non-existent state
- Enumerating all saved torrents
- Input validation
- Data preservation (bitfield, metadata, statistics)

Run tests with:
```bash
dotnet test --filter "FullyQualifiedName~StateManagerTests"
```

## Performance Considerations

### File I/O

- Uses async file operations (`File.WriteAllTextAsync`, `File.ReadAllTextAsync`)
- Minimizes blocking with `SemaphoreSlim` for coordination
- JSON serialization is relatively fast for typical torrent metadata

### Memory Usage

- State files are typically small (< 1 MB for most torrents)
- Bitfield is stored as compact byte array
- No in-memory caching of state files (loaded on demand)

### Disk Space

- Each torrent requires one JSON file
- Typical size: 10-100 KB per torrent
- 1000 torrents ≈ 10-100 MB of disk space

## Future Enhancements

Potential improvements for future versions:

1. **Compression**: Compress state files to reduce disk usage
2. **Encryption**: Encrypt sensitive information in state files
3. **Backup**: Automatic backup of state files
4. **Migration**: Version migration for state file format changes
5. **Cleanup**: Automatic cleanup of old/stale state files
6. **Batching**: Batch multiple state saves to reduce I/O
7. **Database**: Use SQLite for better query performance with many torrents

## Requirements Satisfied

This implementation satisfies the following requirements from the design document:

- **Requirement 8.4**: "WHEN the P2P_System shuts down, THE P2P_System SHALL save the current download state to disk for resume capability"
- **Requirement 8.5**: "WHEN the P2P_System starts, THE P2P_System SHALL load the previous download state and resume incomplete transfers"

## Related Components

- **IBitfield**: Provides the bitfield data that is persisted
- **TorrentMetadata**: Contains the torrent information that is saved
- **PieceManager**: Uses the loaded state to resume piece downloads
- **TorrentEngine**: Orchestrates state saving and loading
