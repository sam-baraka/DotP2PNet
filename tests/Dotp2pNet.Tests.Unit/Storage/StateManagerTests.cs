using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Dotp2pNet.Storage.Bitfield;
using Dotp2pNet.Storage.State;
using Microsoft.Extensions.Logging;
using Xunit;
using DownloadState = Dotp2pNet.Core.Interfaces.DownloadState;

namespace Dotp2pNet.Tests.Unit.Storage;

/// <summary>
/// Unit tests for the StateManager class.
/// </summary>
public class StateManagerTests : IDisposable
{
    private readonly string _testStateDirectory;
    private readonly ILogger<StateManager> _logger;
    private readonly StateManager _stateManager;

    public StateManagerTests()
    {
        // Create a temporary directory for test state files
        _testStateDirectory = Path.Combine(Path.GetTempPath(), "dotp2pnet_test_state_" + Guid.NewGuid());
        Directory.CreateDirectory(_testStateDirectory);

        // Create logger
        _logger = LoggerFactory.Create(builder => { })
            .CreateLogger<StateManager>();

        // Create state manager with test directory
        _stateManager = new StateManager(_logger, _testStateDirectory);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testStateDirectory))
        {
            Directory.Delete(_testStateDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveStateAsync_ValidState_CreatesStateFile()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);
        bitfield.SetPiece(0);
        bitfield.SetPiece(5);

        var downloadDirectory = "/tmp/downloads";
        long bytesDownloaded = 1024;
        long bytesUploaded = 512;

        // Act
        await _stateManager.SaveStateAsync(
            infoHash,
            metadata,
            bitfield,
            downloadDirectory,
            bytesDownloaded,
            bytesUploaded);

        // Assert
        Assert.True(_stateManager.StateExists(infoHash));
    }

    [Fact]
    public async Task LoadStateAsync_ExistingState_ReturnsState()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);
        bitfield.SetPiece(0);
        bitfield.SetPiece(5);
        bitfield.SetPiece(9);

        var downloadDirectory = "/tmp/downloads";
        long bytesDownloaded = 2048;
        long bytesUploaded = 1024;

        // Save state first
        await _stateManager.SaveStateAsync(
            infoHash,
            metadata,
            bitfield,
            downloadDirectory,
            bytesDownloaded,
            bytesUploaded);

        // Act
        var loadedState = await _stateManager.LoadStateAsync(infoHash);

        // Assert
        Assert.NotNull(loadedState);
        Assert.Equal(metadata.Name, loadedState.Metadata.Name);
        Assert.Equal(metadata.TotalSize, loadedState.Metadata.TotalSize);
        Assert.Equal(10, loadedState.PieceCount);
        Assert.Equal(downloadDirectory, loadedState.DownloadDirectory);
        Assert.Equal(bytesDownloaded, loadedState.BytesDownloaded);
        Assert.Equal(bytesUploaded, loadedState.BytesUploaded);

        // Verify bitfield was preserved
        var loadedBitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(loadedState.BitfieldBytes, loadedState.PieceCount);
        Assert.True(loadedBitfield.HasPiece(0));
        Assert.True(loadedBitfield.HasPiece(5));
        Assert.True(loadedBitfield.HasPiece(9));
        Assert.False(loadedBitfield.HasPiece(1));
        Assert.False(loadedBitfield.HasPiece(7));
    }

    [Fact]
    public async Task LoadStateAsync_NonExistentState_ReturnsNull()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        // Act
        var loadedState = await _stateManager.LoadStateAsync(infoHash);

        // Assert
        Assert.Null(loadedState);
    }

    [Fact]
    public async Task SaveStateAsync_OverwritesExistingState()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        var metadata = CreateTestMetadata();
        var bitfield1 = new Dotp2pNet.Storage.Bitfield.Bitfield(10);
        bitfield1.SetPiece(0);

        // Save initial state
        await _stateManager.SaveStateAsync(
            infoHash,
            metadata,
            bitfield1,
            "/tmp/downloads",
            1024,
            512);

        // Update bitfield
        var bitfield2 = new Dotp2pNet.Storage.Bitfield.Bitfield(10);
        bitfield2.SetPiece(0);
        bitfield2.SetPiece(1);
        bitfield2.SetPiece(2);

        // Act - Save updated state
        await _stateManager.SaveStateAsync(
            infoHash,
            metadata,
            bitfield2,
            "/tmp/downloads",
            3072,
            1024);

        // Assert
        var loadedState = await _stateManager.LoadStateAsync(infoHash);
        Assert.NotNull(loadedState);
        Assert.Equal(3072, loadedState.BytesDownloaded);
        Assert.Equal(1024, loadedState.BytesUploaded);

        var loadedBitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(loadedState.BitfieldBytes, loadedState.PieceCount);
        Assert.True(loadedBitfield.HasPiece(0));
        Assert.True(loadedBitfield.HasPiece(1));
        Assert.True(loadedBitfield.HasPiece(2));
    }

    [Fact]
    public async Task DeleteStateAsync_ExistingState_RemovesFile()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);

        await _stateManager.SaveStateAsync(
            infoHash,
            metadata,
            bitfield,
            "/tmp/downloads",
            0,
            0);

        Assert.True(_stateManager.StateExists(infoHash));

        // Act
        await _stateManager.DeleteStateAsync(infoHash);

        // Assert
        Assert.False(_stateManager.StateExists(infoHash));
    }

    [Fact]
    public async Task DeleteStateAsync_NonExistentState_DoesNotThrow()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        // Act & Assert - Should not throw
        await _stateManager.DeleteStateAsync(infoHash);
    }

    [Fact]
    public async Task GetAllSavedTorrentsAsync_MultipleTorrents_ReturnsAllInfoHashes()
    {
        // Arrange
        var infoHash1 = new byte[20];
        var infoHash2 = new byte[20];
        var infoHash3 = new byte[20];
        Random.Shared.NextBytes(infoHash1);
        Random.Shared.NextBytes(infoHash2);
        Random.Shared.NextBytes(infoHash3);

        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);

        await _stateManager.SaveStateAsync(infoHash1, metadata, bitfield, "/tmp/downloads", 0, 0);
        await _stateManager.SaveStateAsync(infoHash2, metadata, bitfield, "/tmp/downloads", 0, 0);
        await _stateManager.SaveStateAsync(infoHash3, metadata, bitfield, "/tmp/downloads", 0, 0);

        // Act
        var savedTorrents = await _stateManager.GetAllSavedTorrentsAsync();

        // Assert
        Assert.Equal(3, savedTorrents.Count);
        Assert.Contains(savedTorrents, hash => hash.SequenceEqual(infoHash1));
        Assert.Contains(savedTorrents, hash => hash.SequenceEqual(infoHash2));
        Assert.Contains(savedTorrents, hash => hash.SequenceEqual(infoHash3));
    }

    [Fact]
    public async Task GetAllSavedTorrentsAsync_NoTorrents_ReturnsEmptyList()
    {
        // Act
        var savedTorrents = await _stateManager.GetAllSavedTorrentsAsync();

        // Assert
        Assert.Empty(savedTorrents);
    }

    [Fact]
    public async Task StateExists_ExistingState_ReturnsTrue()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);

        await _stateManager.SaveStateAsync(infoHash, metadata, bitfield, "/tmp/downloads", 0, 0);

        // Act
        var exists = _stateManager.StateExists(infoHash);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void StateExists_NonExistentState_ReturnsFalse()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        // Act
        var exists = _stateManager.StateExists(infoHash);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task SaveStateAsync_InvalidInfoHash_ThrowsArgumentException()
    {
        // Arrange
        var invalidInfoHash = new byte[10]; // Wrong length
        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _stateManager.SaveStateAsync(
                invalidInfoHash,
                metadata,
                bitfield,
                "/tmp/downloads",
                0,
                0));
    }

    [Fact]
    public async Task SaveStateAsync_NullMetadata_ThrowsArgumentNullException()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _stateManager.SaveStateAsync(
                infoHash,
                null!,
                bitfield,
                "/tmp/downloads",
                0,
                0));
    }

    [Fact]
    public async Task SaveStateAsync_NullBitfield_ThrowsArgumentNullException()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);
        var metadata = CreateTestMetadata();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _stateManager.SaveStateAsync(
                infoHash,
                metadata,
                null!,
                "/tmp/downloads",
                0,
                0));
    }

    [Fact]
    public async Task SaveStateAsync_EmptyDownloadDirectory_ThrowsArgumentException()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);
        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(10);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _stateManager.SaveStateAsync(
                infoHash,
                metadata,
                bitfield,
                "",
                0,
                0));
    }

    [Fact]
    public async Task SaveAndLoad_PreservesAllData()
    {
        // Arrange
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        var metadata = CreateTestMetadata();
        var bitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(100);

        // Set some pieces
        for (int i = 0; i < 100; i += 10)
        {
            bitfield.SetPiece(i);
        }

        var downloadDirectory = "/tmp/test/downloads";
        long bytesDownloaded = 123456789;
        long bytesUploaded = 987654321;

        // Act
        await _stateManager.SaveStateAsync(
            infoHash,
            metadata,
            bitfield,
            downloadDirectory,
            bytesDownloaded,
            bytesUploaded);

        var loadedState = await _stateManager.LoadStateAsync(infoHash);

        // Assert
        Assert.NotNull(loadedState);
        Assert.Equal(metadata.InfoHash, loadedState.Metadata.InfoHash);
        Assert.Equal(metadata.Name, loadedState.Metadata.Name);
        Assert.Equal(metadata.TotalSize, loadedState.Metadata.TotalSize);
        Assert.Equal(metadata.PieceSize, loadedState.Metadata.PieceSize);
        Assert.Equal(100, loadedState.PieceCount); // Bitfield has 100 pieces
        Assert.Equal(downloadDirectory, loadedState.DownloadDirectory);
        Assert.Equal(bytesDownloaded, loadedState.BytesDownloaded);
        Assert.Equal(bytesUploaded, loadedState.BytesUploaded);

        // Verify bitfield
        var loadedBitfield = new Dotp2pNet.Storage.Bitfield.Bitfield(loadedState.BitfieldBytes, loadedState.PieceCount);
        for (int i = 0; i < 100; i++)
        {
            if (i % 10 == 0)
            {
                Assert.True(loadedBitfield.HasPiece(i), $"Piece {i} should be set");
            }
            else
            {
                Assert.False(loadedBitfield.HasPiece(i), $"Piece {i} should not be set");
            }
        }
    }

    private TorrentMetadata CreateTestMetadata()
    {
        var infoHash = new byte[20];
        Random.Shared.NextBytes(infoHash);

        return new TorrentMetadata
        {
            InfoHash = infoHash,
            Name = "test-file.txt",
            TotalSize = 10240,
            PieceSize = 1024,
            PieceHashes = Enumerable.Range(0, 10)
                .Select(_ =>
                {
                    var hash = new byte[32];
                    Random.Shared.NextBytes(hash);
                    return hash;
                })
                .ToList(),
            Files = new List<TorrentFileInfo>
            {
                new TorrentFileInfo
                {
                    Path = new List<string> { "test-file.txt" },
                    Length = 10240
                }
            },
            Trackers = new List<string> { "http://tracker.example.com:8080/announce" }
        };
    }
}
