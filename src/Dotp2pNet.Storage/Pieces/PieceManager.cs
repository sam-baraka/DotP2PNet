using System.Security.Cryptography;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Storage.Pieces;

/// <summary>
/// Manages file chunking, piece storage, and hash verification for torrents.
/// </summary>
/// <remarks>
/// The PieceManager is responsible for:
/// - Splitting files into fixed-size pieces during torrent creation
/// - Computing SHA-256 hashes for each piece and the entire file
/// - Writing received pieces to disk with async I/O
/// - Reading pieces from disk for uploading to peers
/// - Verifying piece integrity using cryptographic hashes
/// </remarks>
public class PieceManager : IPieceManager
{
    private readonly ILogger<PieceManager> _logger;
    private readonly IBitfield _bitfield;
    private readonly TorrentMetadata _metadata;
    private readonly string _downloadDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<int, byte[]> _pieceCache = new();
    private readonly object _cacheLock = new();

    public int TotalPieces => _metadata.PieceCount;
    public int CompletedPieces => _bitfield.CountSetBits();
    public int PieceLength => _metadata.PieceSize;

    /// <summary>
    /// Initializes a new instance of the PieceManager class.
    /// </summary>
    /// <param name="metadata">Torrent metadata containing piece information.</param>
    /// <param name="bitfield">Bitfield tracking which pieces we have.</param>
    /// <param name="downloadDirectory">Directory where files are stored.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public PieceManager(
        TorrentMetadata metadata,
        IBitfield bitfield,
        string downloadDirectory,
        ILogger<PieceManager> logger)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _bitfield = bitfield ?? throw new ArgumentNullException(nameof(bitfield));
        _downloadDirectory = downloadDirectory ?? throw new ArgumentNullException(nameof(downloadDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!_metadata.Validate())
        {
            throw new ArgumentException("Invalid torrent metadata", nameof(metadata));
        }

        // Ensure download directory exists
        Directory.CreateDirectory(_downloadDirectory);
    }

    /// <summary>
    /// Creates a torrent from a file by splitting it into pieces and computing hashes.
    /// </summary>
    /// <param name="filePath">Path to the file to create a torrent from.</param>
    /// <param name="pieceSize">Size of each piece in bytes (default 256 KB).</param>
    /// <param name="trackers">Optional list of tracker URLs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>TorrentMetadata containing all piece information and hashes.</returns>
    public static async Task<TorrentMetadata> CreateTorrentAsync(
        string filePath,
        int pieceSize = 262144,
        List<string>? trackers = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        if (pieceSize < 16384 || pieceSize > 16777216)
        {
            throw new ArgumentException("Piece size must be between 16 KB and 16 MB", nameof(pieceSize));
        }

        var fileInfo = new FileInfo(filePath);
        var fileName = fileInfo.Name;
        var fileSize = fileInfo.Length;

        // Calculate number of pieces
        var pieceCount = (int)Math.Ceiling((double)fileSize / pieceSize);
        var pieceHashes = new List<byte[]>(pieceCount);

        // Read file and compute piece hashes
        using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[pieceSize];
        var allFileBytes = new List<byte>((int)fileSize);

        for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
        {
            ct.ThrowIfCancellationRequested();

            // Determine how many bytes to read for this piece
            var bytesToRead = (int)Math.Min(pieceSize, fileSize - (pieceIndex * (long)pieceSize));
            var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);

            if (bytesRead != bytesToRead)
            {
                throw new IOException($"Failed to read expected bytes from file. Expected: {bytesToRead}, Read: {bytesRead}");
            }

            // Compute SHA-256 hash for this piece
            var pieceData = buffer.AsSpan(0, bytesRead).ToArray();
            var pieceHash = SHA256.HashData(pieceData);
            pieceHashes.Add(pieceHash);

            // Collect bytes for root hash computation
            allFileBytes.AddRange(pieceData);
        }

        // Compute root hash (SHA-256 of entire file)
        var rootHash = SHA256.HashData(allFileBytes.ToArray());

        // Create torrent metadata
        var metadata = new TorrentMetadata
        {
            InfoHash = rootHash.Take(20).ToArray(), // Use first 20 bytes as info hash
            Name = fileName,
            TotalSize = fileSize,
            PieceSize = pieceSize,
            PieceHashes = pieceHashes,
            Files = new List<TorrentFileInfo>
            {
                new TorrentFileInfo
                {
                    Path = new List<string> { fileName },
                    Length = fileSize
                }
            },
            Trackers = trackers ?? new List<string>()
        };

        return metadata;
    }

    public bool HasPiece(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= TotalPieces)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        return _bitfield.HasPiece(pieceIndex);
    }

    public int SelectPiece(IBitfield peerBitfield)
    {
        if (peerBitfield == null)
        {
            throw new ArgumentNullException(nameof(peerBitfield));
        }

        // Simple strategy: find first piece peer has that we don't have
        for (int i = 0; i < TotalPieces; i++)
        {
            if (peerBitfield.HasPiece(i) && !_bitfield.HasPiece(i))
            {
                return i;
            }
        }

        return -1; // No piece needed from this peer
    }

    public async Task<bool> StorePieceAsync(int pieceIndex, byte[] data, CancellationToken ct)
    {
        if (pieceIndex < 0 || pieceIndex >= TotalPieces)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        // Verify piece hash before storing
        if (!await VerifyPieceAsync(pieceIndex, data, ct))
        {
            _logger.LogWarning(
                "Piece {PieceIndex} failed hash verification. Expected: {ExpectedHash}, Got: {ActualHash}",
                pieceIndex,
                Convert.ToHexString(_metadata.PieceHashes[pieceIndex]),
                Convert.ToHexString(SHA256.HashData(data)));
            return false;
        }

        // Write piece to disk
        await WritePieceAsync(pieceIndex, data, ct);

        // Mark piece as complete in bitfield
        _bitfield.SetPiece(pieceIndex);

        _logger.LogInformation(
            "Successfully stored piece {PieceIndex}/{TotalPieces} ({Progress:F2}%)",
            pieceIndex,
            TotalPieces,
            (CompletedPieces * 100.0) / TotalPieces);

        return true;
    }

    public async Task<byte[]> GetPieceAsync(int pieceIndex, CancellationToken ct)
    {
        if (pieceIndex < 0 || pieceIndex >= TotalPieces)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        if (!_bitfield.HasPiece(pieceIndex))
        {
            throw new InvalidOperationException($"Piece {pieceIndex} is not available");
        }

        // Check cache first
        lock (_cacheLock)
        {
            if (_pieceCache.TryGetValue(pieceIndex, out var cachedData))
            {
                _logger.LogDebug("Piece {PieceIndex} retrieved from cache", pieceIndex);
                return cachedData;
            }
        }

        // Read from disk
        var data = await ReadPieceAsync(pieceIndex, ct);

        // Add to cache (simple cache, no eviction for now)
        lock (_cacheLock)
        {
            if (!_pieceCache.ContainsKey(pieceIndex))
            {
                _pieceCache[pieceIndex] = data;
            }
        }

        return data;
    }

    public IBitfield GetBitfield()
    {
        return _bitfield;
    }

    /// <summary>
    /// Writes a piece to disk at the appropriate file offset.
    /// </summary>
    /// <param name="pieceIndex">Index of the piece to write.</param>
    /// <param name="data">Piece data to write.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task WritePieceAsync(int pieceIndex, byte[] data, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            // For single-file torrents, write directly to the file
            var filePath = Path.Combine(_downloadDirectory, _metadata.Files[0].FullPath);
            var fileOffset = (long)pieceIndex * _metadata.PieceSize;

            // Ensure parent directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Open or create file
            using var fileStream = new FileStream(
                filePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            // Seek to the correct position
            fileStream.Seek(fileOffset, SeekOrigin.Begin);

            // Write the piece data
            await fileStream.WriteAsync(data, ct);
            await fileStream.FlushAsync(ct);

            _logger.LogDebug(
                "Wrote piece {PieceIndex} to {FilePath} at offset {Offset} ({Size} bytes)",
                pieceIndex,
                filePath,
                fileOffset,
                data.Length);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads a piece from disk.
    ///   /ummary>
    /// <param name="pieceIndex">Index of the piece to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Piece data.</returns>
    private async Task<byte[]> ReadPieceAsync(int pieceIndex, CancellationToken ct)
    {
        var filePath = Path.Combine(_downloadDirectory, _metadata.Files[0].FullPath);
        var fileOffset = (long)pieceIndex * _metadata.PieceSize;

        // Determine piece size (last piece might be smaller)
        var isLastPiece = pieceIndex == TotalPieces - 1;
        var pieceSize = isLastPiece
            ? (int)(_metadata.TotalSize - ((long)pieceIndex * _metadata.PieceSize))
            : _metadata.PieceSize;

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        // Seek to the correct position
        fileStream.Seek(fileOffset, SeekOrigin.Begin);

        // Read the piece data
        var buffer = new byte[pieceSize];
        var bytesRead = await fileStream.ReadAsync(buffer, ct);

        if (bytesRead != pieceSize)
        {
            throw new IOException($"Failed to read expected bytes. Expected: {pieceSize}, Read: {bytesRead}");
        }

        _logger.LogDebug(
            "Read piece {PieceIndex} from {FilePath} at offset {Offset} ({Size} bytes)",
            pieceIndex,
            filePath,
            fileOffset,
            bytesRead);

        return buffer;
    }

    /// <summary>
    /// Verifies a piece's integrity by comparing its hash against the expected hash.
    /// </summary>
    /// <param name="pieceIndex">Index of the piece to verify.</param>
    /// <param name="data">Piece data to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the hash matches, false otherwise.</returns>
    private async Task<bool> VerifyPieceAsync(int pieceIndex, byte[] data, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var expectedHash = _metadata.PieceHashes[pieceIndex];
            var actualHash = SHA256.HashData(data);

            var isValid = expectedHash.SequenceEqual(actualHash);

            if (isValid)
            {
                _logger.LogDebug("Piece {PieceIndex} hash verification passed", pieceIndex);
            }
            else
            {
                _logger.LogWarning(
                    "Piece {PieceIndex} hash verification failed. Expected: {Expected}, Actual: {Actual}",
                    pieceIndex,
                    Convert.ToHexString(expectedHash),
                    Convert.ToHexString(actualHash));
            }

            return isValid;
        }, ct);
    }
}
//