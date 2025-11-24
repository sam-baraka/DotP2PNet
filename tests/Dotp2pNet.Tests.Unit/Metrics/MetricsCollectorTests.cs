using Dotp2pNet.Core.Metrics;
using Xunit;

namespace Dotp2pNet.Tests.Unit.Metrics;

/// <summary>
/// Tests for the MetricsCollector class.
/// </summary>
public class MetricsCollectorTests
{
    private readonly byte[] _testInfoHash = new byte[20] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };

    [Fact]
    public void RecordDownload_UpdatesTotalDownloaded()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordDownload(_testInfoHash, 1024);
        collector.RecordDownload(_testInfoHash, 2048);

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.NotNull(metrics);
        Assert.Equal(3072, metrics.TotalDownloaded);
    }

    [Fact]
    public void RecordUpload_UpdatesTotalUploaded()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordUpload(_testInfoHash, 512);
        collector.RecordUpload(_testInfoHash, 1024);

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.NotNull(metrics);
        Assert.Equal(1536, metrics.TotalUploaded);
    }

    [Fact]
    public void RecordPieceCompleted_TracksUniquePieces()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordPieceCompleted(_testInfoHash, 0);
        collector.RecordPieceCompleted(_testInfoHash, 1);
        collector.RecordPieceCompleted(_testInfoHash, 0); // Duplicate

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.NotNull(metrics);
        Assert.Equal(2, metrics.PiecesCompleted);
        Assert.Contains(0, metrics.CompletedPieceIndices);
        Assert.Contains(1, metrics.CompletedPieceIndices);
    }

    [Fact]
    public void RecordHashVerification_TracksSuccessAndFailure()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordHashVerification(_testInfoHash, true);
        collector.RecordHashVerification(_testInfoHash, true);
        collector.RecordHashVerification(_testInfoHash, false);

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.NotNull(metrics);
        Assert.Equal(2, metrics.HashVerificationSuccesses);
        Assert.Equal(1, metrics.HashVerificationFailures);
        Assert.Equal(66.67, metrics.HashVerificationSuccessRate, 2);
    }

    [Fact]
    public void RecordHashVerification_ReturnsHundredPercentWhenNoFailures()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordHashVerification(_testInfoHash, true);
        collector.RecordHashVerification(_testInfoHash, true);

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.NotNull(metrics);
        Assert.Equal(100.0, metrics.HashVerificationSuccessRate);
    }

    [Fact]
    public void RecordPeerConnections_UpdatesCounts()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordPeerConnections(_testInfoHash, 5, 20);

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.NotNull(metrics);
        Assert.Equal(5, metrics.ActiveConnections);
        Assert.Equal(20, metrics.TotalPeers);
    }

    [Fact]
    public void RecordDhtRoutingTableSize_UpdatesSize()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordDhtRoutingTableSize(156);

        // Assert
        Assert.Equal(156, collector.DhtRoutingTableSize);
        Assert.NotEqual(DateTime.MinValue, collector.LastDhtUpdate);
    }

    [Fact]
    public async Task GetDownloadRate_CalculatesSlidingWindowRate()
    {
        // Arrange
        var collector = new MetricsCollector(TimeSpan.FromSeconds(2));

        // Act
        collector.RecordDownload(_testInfoHash, 1000);
        await Task.Delay(100);
        collector.RecordDownload(_testInfoHash, 1000);
        await Task.Delay(100);
        collector.RecordDownload(_testInfoHash, 1000);

        // Assert
        var rate = collector.GetDownloadRate();
        Assert.True(rate > 0, "Download rate should be greater than 0");
        Assert.True(rate < 20000, "Download rate should be reasonable for the test data");
    }

    [Fact]
    public async Task GetUploadRate_CalculatesSlidingWindowRate()
    {
        // Arrange
        var collector = new MetricsCollector(TimeSpan.FromSeconds(2));

        // Act
        collector.RecordUpload(_testInfoHash, 500);
        await Task.Delay(100);
        collector.RecordUpload(_testInfoHash, 500);
        await Task.Delay(100);
        collector.RecordUpload(_testInfoHash, 500);

        // Assert
        var rate = collector.GetUploadRate();
        Assert.True(rate > 0, "Upload rate should be greater than 0");
        Assert.True(rate < 10000, "Upload rate should be reasonable for the test data");
    }

    [Fact]
    public async Task SlidingWindow_RemovesOldSamples()
    {
        // Arrange
        var collector = new MetricsCollector(TimeSpan.FromMilliseconds(500));

        // Act
        collector.RecordDownload(_testInfoHash, 1000);
        await Task.Delay(600); // Wait for samples to age out
        var rate = collector.GetDownloadRate();

        // Assert
        Assert.Equal(0, rate); // Old samples should be removed
    }

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        // Arrange
        var collector = new MetricsCollector();
        collector.RecordDownload(_testInfoHash, 1024);
        collector.RecordUpload(_testInfoHash, 512);
        collector.RecordDhtRoutingTableSize(100);

        // Act
        collector.Reset();

        // Assert
        Assert.Null(collector.GetTorrentMetrics(_testInfoHash));
        Assert.Equal(0, collector.DhtRoutingTableSize);
        Assert.Equal(0, collector.GetDownloadRate());
        Assert.Equal(0, collector.GetUploadRate());
    }

    [Fact]
    public void RemoveTorrentMetrics_RemovesSpecificTorrent()
    {
        // Arrange
        var collector = new MetricsCollector();
        var infoHash1 = new byte[20] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        var infoHash2 = new byte[20] { 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

        collector.RecordDownload(infoHash1, 1024);
        collector.RecordDownload(infoHash2, 2048);

        // Act
        collector.RemoveTorrentMetrics(infoHash1);

        // Assert
        Assert.Null(collector.GetTorrentMetrics(infoHash1));
        Assert.NotNull(collector.GetTorrentMetrics(infoHash2));
    }

    [Fact]
    public void GetAllTorrentMetrics_ReturnsAllTorrents()
    {
        // Arrange
        var collector = new MetricsCollector();
        var infoHash1 = new byte[20] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        var infoHash2 = new byte[20] { 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

        collector.RecordDownload(infoHash1, 1024);
        collector.RecordDownload(infoHash2, 2048);

        // Act
        var allMetrics = collector.GetAllTorrentMetrics();

        // Assert
        Assert.Equal(2, allMetrics.Count);
    }

    [Fact]
    public void TorrentMetrics_ToString_FormatsCorrectly()
    {
        // Arrange
        var collector = new MetricsCollector();
        collector.RecordDownload(_testInfoHash, 1024 * 1024); // 1 MB
        collector.RecordUpload(_testInfoHash, 512 * 1024); // 512 KB
        collector.RecordPieceCompleted(_testInfoHash, 0);
        collector.RecordHashVerification(_testInfoHash, true);
        collector.RecordPeerConnections(_testInfoHash, 5, 20);

        // Act
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        var str = metrics?.ToString();

        // Assert
        Assert.NotNull(str);
        Assert.Contains("1.00 MB", str);
        Assert.Contains("512.00 KB", str);
        Assert.Contains("Pieces: 1", str);
        Assert.Contains("100.0%", str);
        Assert.Contains("5/20", str);
    }

    [Fact]
    public void RecordDownload_IgnoresZeroOrNegativeBytes()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordDownload(_testInfoHash, 0);
        collector.RecordDownload(_testInfoHash, -100);

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.Null(metrics); // No metrics should be created for zero/negative values
    }

    [Fact]
    public void RecordUpload_IgnoresZeroOrNegativeBytes()
    {
        // Arrange
        var collector = new MetricsCollector();

        // Act
        collector.RecordUpload(_testInfoHash, 0);
        collector.RecordUpload(_testInfoHash, -100);

        // Assert
        var metrics = collector.GetTorrentMetrics(_testInfoHash);
        Assert.Null(metrics); // No metrics should be created for zero/negative values
    }
}
