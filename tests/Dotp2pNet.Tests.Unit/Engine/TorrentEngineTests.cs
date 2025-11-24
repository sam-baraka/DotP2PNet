using System.Net;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Dotp2pNet.Orchestration.Engine;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Dotp2pNet.Tests.Unit.Engine;

/// <summary>
/// Unit tests for the TorrentEngine class.
/// </summary>
public class TorrentEngineTests
{
    [Fact]
    public async Task StartDownloadAsync_WithValidMetadata_CreatesActiveTorrent()
    {
        // Arrange
        var engine = CreateEngine();
        var metadata = CreateValidMetadata();
        var savePath = "/tmp/downloads";

        // Act
        await engine.StartDownloadAsync(metadata, savePath);
        await Task.Delay(200); // Give more time for coordination task to start

        // Assert
        var status = engine.GetStatus(metadata.InfoHash);
        Assert.NotNull(status);
        Assert.Equal(metadata.Name, status.Name);
        Assert.Equal(metadata.TotalSize, status.TotalSize);
        // State should be one of the active states (not Stopped or Error)
        Assert.NotEqual(TorrentState.Stopped, status.State);
        Assert.NotEqual(TorrentState.Error, status.State);
    }

    [Fact]
    public async Task StartDownloadAsync_WithNullMetadata_ThrowsArgumentNullException()
    {
        // Arrange
        var engine = CreateEngine();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => engine.StartDownloadAsync(null!, "/tmp/downloads"));
    }

    [Fact]
    public async Task GetStatus_WithNonExistentTorrent_ReturnsNull()
    {
        // Arrange
        var engine = CreateEngine();
        var infoHash = new byte[20];

        // Act
        var status = engine.GetStatus(infoHash);

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public async Task PauseAsync_WithActiveTorrent_PausesTorrent()
    {
        // Arrange
        var engine = CreateEngine();
        var metadata = CreateValidMetadata();
        await engine.StartDownloadAsync(metadata, "/tmp/downloads");
        await Task.Delay(100);

        // Act
        await engine.PauseAsync(metadata.InfoHash);

        // Assert
        var status = engine.GetStatus(metadata.InfoHash);
        Assert.NotNull(status);
        Assert.Equal(TorrentState.Paused, status.State);
    }

    [Fact]
    public async Task ResumeAsync_WithPausedTorrent_ResumesTorrent()
    {
        // Arrange
        var engine = CreateEngine();
        var metadata = CreateValidMetadata();
        await engine.StartDownloadAsync(metadata, "/tmp/downloads");
        await Task.Delay(100);
        await engine.PauseAsync(metadata.InfoHash);

        // Act
        await engine.ResumeAsync(metadata.InfoHash);

        // Assert
        var status = engine.GetStatus(metadata.InfoHash);
        Assert.NotNull(status);
        Assert.NotEqual(TorrentState.Paused, status.State);
    }

    [Fact]
    public async Task StopAsync_WithActiveTorrent_RemovesTorrent()
    {
        // Arrange
        var engine = CreateEngine();
        var metadata = CreateValidMetadata();
        await engine.StartDownloadAsync(metadata, "/tmp/downloads");
        await Task.Delay(100);

        // Act
        await engine.StopAsync(metadata.InfoHash);

        // Assert
        var status = engine.GetStatus(metadata.InfoHash);
        Assert.Null(status);
    }

    private static TorrentEngine CreateEngine()
    {
        return new TorrentEngine(
            new TestConnectionManager(),
            new TestTrackerClient(),
            new TestDhtNode(),
            new TestPieceManager(),
            new TestPieceSelector(),
            new TestPeerSelector(),
            new TestLogger<TorrentEngine>());
    }

    private static TorrentMetadata CreateValidMetadata(string name = "test-torrent")
    {
        var infoHash = new byte[20];
        new Random().NextBytes(infoHash);

        var pieceHash = new byte[32];
        new Random().NextBytes(pieceHash);

        return new TorrentMetadata
        {
            InfoHash = infoHash,
            Name = name,
            TotalSize = 1024 * 1024,
            PieceSize = 256 * 1024,
            PieceHashes = new List<byte[]> { pieceHash, pieceHash, pieceHash, pieceHash },
            Files = new List<TorrentFileInfo>
            {
                new TorrentFileInfo
                {
                    Path = new List<string> { $"{name}.bin" },
                    Length = 1024 * 1024
                }
            },
            Trackers = new List<string> { "http://tracker.example.com:8080/announce" }
        };
    }
}
