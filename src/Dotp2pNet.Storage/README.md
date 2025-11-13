# Dotp2pNet.Storage

This layer handles file chunking, piece management, and storage operations for the P2P file sharing system.

## Components

### PieceManager

The `PieceManager` class is responsible for:

- **File Chunking**: Splitting files into fixed-size pieces (default 256 KB)
- **Hash Computation**: Computing SHA-256 hashes for each piece and the entire file
- **Piece Storage**: Writing received pieces to disk with async I/O
- **Piece Retrieval**: Reading pieces from disk for uploading to peers
- **Hash Verification**: Verifying piece integrity using cryptographic hashes

#### Key Concepts

**Piece Size Strategy**

The default piece size of 256 KB (262,144 bytes) provides a good balance between:
- **Overhead**: Smaller pieces mean more hash storage and more network messages
- **Granularity**: Larger pieces mean less flexibility in piece selection and longer verification times
- **Network Efficiency**: 256 KB pieces work well with typical network MTU sizes

**SHA-256 Verification**

Each piece is hashed using SHA-256 (32 bytes per hash):
1. When creating a torrent, each piece is hashed as it's read from the file
2. When receiving a piece, the hash is computed and compared to the expected hash
3. Only pieces with valid hashes are written to disk and marked as complete
4. This ensures data integrity even when receiving from untrusted peers

**Async I/O**

All file operations use async I/O to avoid blocking threads:
- `FileStream` with `useAsync: true` for optimal async performance
- `SemaphoreSlim` for coordinating concurrent writes
- Proper cancellation token support throughout

#### Usage Example

```csharp
// Create a torrent from a file
var metadata = await PieceManager.CreateTorrentAsync(
    filePath: "/path/to/file.iso",
    pieceSize: 262144, // 256 KB
    trackers: new List<string> { "http://tracker.example.com" }
);

// Initialize PieceManager for downloading
var bitfield = new Bitfield(metadata.PieceCount);
var pieceManager = new PieceManager(
    metadata,
    bitfield,
    downloadDirectory: "./downloads",
    logger
);

// Store a received piece
var pieceData = /* received from peer */;
var success = await pieceManager.StorePieceAsync(0, pieceData, ct);

// Read a piece to send to a peer
if (pieceManager.HasPiece(5))
{
    var data = await pieceManager.GetPieceAsync(5, ct);
    // Send data to peer
}
```

### Bitfield

The `Bitfield` class provides efficient tracking of piece availability:

- **Compact Storage**: Uses `BitArray` for bit-level storage (1 bit per piece)
- **Thread-Safe**: All operations are protected with locks
- **Wire Format**: Can serialize to/from byte arrays for network transmission

#### Usage Example

```csharp
// Create a bitfield for 1000 pieces
var bitfield = new Bitfield(1000);

// Mark pieces as available
bitfield.SetPiece(0);
bitfield.SetPiece(5);
bitfield.SetPiece(10);

// Check availability
if (bitfield.HasPiece(5))
{
    Console.WriteLine("We have piece 5");
}

// Get statistics
Console.WriteLine($"Progress: {bitfield.CountSetBits()}/{bitfield.Length}");

// Serialize for network transmission
var bytes = bitfield.ToBytes();

// Deserialize from network
var receivedBitfield = new Bitfield(bytes, 1000);
```

## Design Decisions

### Single-File Support (Phase 1)

The current implementation focuses on single-file torrents for simplicity:
- Multi-file support can be added later by extending the file offset calculations
- The architecture supports this through the `TorrentFileInfo` list in metadata

### Simple Piece Selection

The `SelectPiece` method uses a simple first-available strategy:
- More sophisticated strategies (rarest-first, endgame mode) will be implemented in the Orchestration layer
- This keeps the Storage layer focused on storage concerns

### In-Memory Piece Cache

A simple dictionary-based cache is used for frequently accessed pieces:
- No eviction policy in this phase (suitable for learning/testing)
- Production systems would implement LRU or similar eviction strategies

### Thread Safety

Thread safety is achieved through:
- `SemaphoreSlim` for write coordination (prevents concurrent writes to the same file)
- `lock` statements for bitfield operations
- `lock` for cache access

## Performance Considerations

### Memory Usage

- Piece cache grows unbounded (acceptable for small torrents)
- Consider implementing LRU eviction for large torrents
- Each piece hash is 32 bytes (SHA-256)

### Disk I/O

- All operations use async I/O with proper buffer sizes (81,920 bytes)
- Files are opened with appropriate sharing modes
- Writes are flushed to ensure durability

### Hash Computation

- SHA-256 is computed using the built-in `System.Security.Cryptography` APIs
- Hash verification runs on the thread pool to avoid blocking
- Consider using hardware acceleration if available

## Testing

To test the PieceManager:

```csharp
// Create a test file
var testFile = Path.GetTempFileName();
await File.WriteAllBytesAsync(testFile, new byte[1024 * 1024]); // 1 MB

// Create torrent
var metadata = await PieceManager.CreateTorrentAsync(testFile, 262144);

// Verify metadata
Assert.Equal(4, metadata.PieceCount); // 1 MB / 256 KB = 4 pieces
Assert.Equal(4, metadata.PieceHashes.Count);

// Test piece storage and retrieval
var bitfield = new Bitfield(metadata.PieceCount);
var manager = new PieceManager(metadata, bitfield, "./test-downloads", logger);

var pieceData = new byte[262144];
var success = await manager.StorePieceAsync(0, pieceData, CancellationToken.None);
Assert.True(success);
Assert.True(manager.HasPiece(0));
```

## Future Enhancements

- [ ] Multi-file torrent support
- [ ] LRU cache eviction policy
- [ ] Memory-mapped file support for large torrents
- [ ] Sparse file support for incomplete downloads
- [ ] Piece priority system
- [ ] Disk space checking before writes
