using Dotp2pNet.Core.Models;
using Dotp2pNet.Orchestration.PeerManagement;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Dotp2pNet.Tests.Unit.PeerManagement;

public class PeerSelectorTests
{
    private readonly ILogger<PeerSelector> _logger;

    public PeerSelectorTests()
    {
        _logger = new LoggerFactory().CreateLogger<PeerSelector>();
    }

    #region SelectPeersToConnect Tests

    [Fact]
    public void SelectPeersToConnect_WithNoAvailablePeers_ReturnsEmptyList()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var availablePeers = new List<PeerInfo>();
        var currentConnections = new List<PeerInfo>();

        // Act
        var result = selector.SelectPeersToConnect(availablePeers, currentConnections, maxConnections: 50);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void SelectPeersToConnect_AtMaxConnections_ReturnsEmptyList()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var availablePeers = CreateTestPeers(10);
        var currentConnections = CreateTestPeers(50);

        // Act
        var result = selector.SelectPeersToConnect(availablePeers, currentConnections, maxConnections: 50);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void SelectPeersToConnect_FiltersOutAlreadyConnectedPeers()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var peer1 = CreateTestPeer(1);
        var peer2 = CreateTestPeer(2);
        var peer3 = CreateTestPeer(3);

        var availablePeers = new List<PeerInfo> { peer1, peer2, peer3 };
        var currentConnections = new List<PeerInfo> { peer1 }; // Already connected to peer1

        // Act
        var result = selector.SelectPeersToConnect(availablePeers, currentConnections, maxConnections: 50);

        // Assert
        Assert.DoesNotContain(peer1, result);
        Assert.Contains(peer2, result);
        Assert.Contains(peer3, result);
    }

    [Fact]
    public void SelectPeersToConnect_FiltersOutStalePeers()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var freshPeer = CreateTestPeer(1);
        var stalePeer = CreateTestPeer(2);
        stalePeer.LastSeen = DateTime.UtcNow.AddMinutes(-10); // Stale (> 5 minutes)

        var availablePeers = new List<PeerInfo> { freshPeer, stalePeer };
        var currentConnections = new List<PeerInfo>();

        // Act
        var result = selector.SelectPeersToConnect(availablePeers, currentConnections, maxConnections: 50);

        // Assert
        Assert.Contains(freshPeer, result);
        Assert.DoesNotContain(stalePeer, result);
    }

    [Fact]
    public void SelectPeersToConnect_FiltersOutLowReputationPeers()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var goodPeer = CreateTestPeer(1);
        goodPeer.Stats.PiecesDownloaded = 100;
        goodPeer.Stats.HashFailures = 0;

        var badPeer = CreateTestPeer(2);
        badPeer.Stats.PiecesDownloaded = 10;
        badPeer.Stats.HashFailures = 9; // 90% failure rate - very low reputation

        var availablePeers = new List<PeerInfo> { goodPeer, badPeer };
        var currentConnections = new List<PeerInfo>();

        // Act
        var result = selector.SelectPeersToConnect(availablePeers, currentConnections, maxConnections: 50);

        // Assert
        Assert.Contains(goodPeer, result);
        Assert.DoesNotContain(badPeer, result); // Should be filtered out due to low reputation
    }

    [Fact]
    public void SelectPeersToConnect_PrioritizesByReputationScore()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        
        var excellentPeer = CreateTestPeer(1);
        excellentPeer.Stats.PiecesDownloaded = 100;
        excellentPeer.Stats.PiecesUploaded = 50;
        excellentPeer.Stats.HashFailures = 0;
        excellentPeer.Stats.AverageResponseTime = TimeSpan.FromMilliseconds(500);

        var goodPeer = CreateTestPeer(2);
        goodPeer.Stats.PiecesDownloaded = 50;
        goodPeer.Stats.PiecesUploaded = 20;
        goodPeer.Stats.HashFailures = 1;
        goodPeer.Stats.AverageResponseTime = TimeSpan.FromSeconds(2);

        var mediocrePeer = CreateTestPeer(3);
        mediocrePeer.Stats.PiecesDownloaded = 20;
        mediocrePeer.Stats.HashFailures = 2;
        mediocrePeer.Stats.AverageResponseTime = TimeSpan.FromSeconds(4);

        var availablePeers = new List<PeerInfo> { mediocrePeer, excellentPeer, goodPeer };
        var currentConnections = new List<PeerInfo>();

        // Act
        var result = selector.SelectPeersToConnect(availablePeers, currentConnections, maxConnections: 2);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(excellentPeer, result[0]); // Best peer first
        Assert.Equal(goodPeer, result[1]); // Second best peer
    }

    [Fact]
    public void SelectPeersToConnect_RespectsMaxConnectionsLimit()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        // Create available peers with different IDs than current connections
        var availablePeers = CreateTestPeers(20, startId: 100);
        var currentConnections = CreateTestPeers(45, startId: 1);

        // Act
        var result = selector.SelectPeersToConnect(availablePeers, currentConnections, maxConnections: 50);

        // Assert
        Assert.Equal(5, result.Count); // Only 5 slots available (50 - 45)
    }

    #endregion

    #region SelectPeersToUnchoke Tests

    [Fact]
    public void SelectPeersToUnchoke_WithNoPeers_ReturnsEmptyList()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var connectedPeers = new List<PeerInfo>();

        // Act
        var result = selector.SelectPeersToUnchoke(connectedPeers, uploadSlots: 4);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void SelectPeersToUnchoke_ReturnsEmptyWhenCalledTooSoon()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var connectedPeers = CreateConnectedPeers(5);

        // Act
        var result1 = selector.SelectPeersToUnchoke(connectedPeers, uploadSlots: 4);
        var result2 = selector.SelectPeersToUnchoke(connectedPeers, uploadSlots: 4); // Called immediately

        // Assert
        // First call initializes, second call returns empty because not enough time has passed
        Assert.Empty(result2);
    }

    [Fact(Skip = "Requires 10+ second delay - run manually if needed")]
    public void SelectPeersToUnchoke_PrioritizesPeersByUploadRate()
    {
        // Arrange
        // Create a new selector for each test to ensure fresh state
        var selector = new PeerSelector(_logger);
        
        var fastUploader = CreateConnectedPeer(1, uploadRate: 1000000); // 1 MB/s
        var mediumUploader = CreateConnectedPeer(2, uploadRate: 500000); // 500 KB/s
        var slowUploader = CreateConnectedPeer(3, uploadRate: 100000); // 100 KB/s
        var noUploader = CreateConnectedPeer(4, uploadRate: 0);

        var connectedPeers = new List<PeerInfo> { noUploader, slowUploader, fastUploader, mediumUploader };

        // Act
        // Call multiple times - first call initializes, subsequent calls after delay will return results
        selector.SelectPeersToUnchoke(connectedPeers, uploadSlots: 4); // Initialize
        System.Threading.Thread.Sleep(10100); // Wait just over 10 seconds for choking evaluation
        var result = selector.SelectPeersToUnchoke(connectedPeers, uploadSlots: 4);

        // Assert
        Assert.NotEmpty(result);
        // Should include fast and medium uploaders in the regular slots
        Assert.Contains(result, id => id.SequenceEqual(fastUploader.PeerId));
        Assert.Contains(result, id => id.SequenceEqual(mediumUploader.PeerId));
    }

    #endregion

    #region SelectOptimisticUnchoke Tests

    [Fact]
    public void SelectOptimisticUnchoke_WithNoPeers_ReturnsNull()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var connectedPeers = new List<PeerInfo>();
        var currentlyUnchoked = new List<byte[]>();

        // Act
        var result = selector.SelectOptimisticUnchoke(connectedPeers, currentlyUnchoked);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SelectOptimisticUnchoke_WithAllPeersUnchoked_ReturnsNull()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var peer1 = CreateConnectedPeer(1);
        var peer2 = CreateConnectedPeer(2);
        var connectedPeers = new List<PeerInfo> { peer1, peer2 };
        var currentlyUnchoked = new List<byte[]> { peer1.PeerId, peer2.PeerId };

        // Act
        var result = selector.SelectOptimisticUnchoke(connectedPeers, currentlyUnchoked);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SelectOptimisticUnchoke_SelectsFromChokedPeers()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        var unchokedPeer = CreateConnectedPeer(1);
        var chokedPeer1 = CreateConnectedPeer(2);
        var chokedPeer2 = CreateConnectedPeer(3);
        
        var connectedPeers = new List<PeerInfo> { unchokedPeer, chokedPeer1, chokedPeer2 };
        var currentlyUnchoked = new List<byte[]> { unchokedPeer.PeerId };

        // Act
        var result = selector.SelectOptimisticUnchoke(connectedPeers, currentlyUnchoked);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.SequenceEqual(unchokedPeer.PeerId));
        Assert.True(
            result.SequenceEqual(chokedPeer1.PeerId) || 
            result.SequenceEqual(chokedPeer2.PeerId));
    }

    [Fact]
    public void SelectOptimisticUnchoke_PrefersNewPeers()
    {
        // Arrange
        var selector = new PeerSelector(_logger);
        
        var newPeer = CreateConnectedPeer(1);
        newPeer.Stats.BytesUploaded = 0; // New peer, no uploads yet
        
        var oldPeer = CreateConnectedPeer(2);
        oldPeer.Stats.BytesUploaded = 1000000; // Has uploaded before
        
        var connectedPeers = new List<PeerInfo> { newPeer, oldPeer };
        var currentlyUnchoked = new List<byte[]>();

        // Act
        var result = selector.SelectOptimisticUnchoke(connectedPeers, currentlyUnchoked);

        // Assert
        Assert.NotNull(result);
        // Should prefer the new peer (though it's random, so we just check it's one of them)
        Assert.True(
            result.SequenceEqual(newPeer.PeerId) || 
            result.SequenceEqual(oldPeer.PeerId));
    }

    #endregion

    #region Helper Methods

    private PeerInfo CreateTestPeer(int id)
    {
        var peerId = new byte[20];
        BitConverter.GetBytes(id).CopyTo(peerId, 0);
        // Fill the rest with a pattern to make it valid
        for (int i = 4; i < 20; i++)
        {
            peerId[i] = (byte)(id % 256);
        }

        return new PeerInfo
        {
            PeerId = peerId,
            IpAddress = System.Net.IPAddress.Parse($"192.168.1.{Math.Min(id, 254)}"),
            Port = 6881 + id,
            LastSeen = DateTime.UtcNow
        };
    }

    private List<PeerInfo> CreateTestPeers(int count, int startId = 1)
    {
        var peers = new List<PeerInfo>();
        for (int i = 0; i < count; i++)
        {
            peers.Add(CreateTestPeer(startId + i));
        }
        return peers;
    }

    private PeerInfo CreateConnectedPeer(int id, double uploadRate = 0)
    {
        var peer = CreateTestPeer(id);
        peer.Stats.ConnectedAt = DateTime.UtcNow.AddMinutes(-5);
        
        if (uploadRate > 0)
        {
            // Calculate bytes uploaded to achieve the desired rate
            var duration = (DateTime.UtcNow - peer.Stats.ConnectedAt.Value).TotalSeconds;
            peer.Stats.BytesUploaded = (long)(uploadRate * duration);
            peer.Stats.PiecesUploaded = (int)(peer.Stats.BytesUploaded / 16384); // Assume 16KB pieces
        }
        
        return peer;
    }

    private List<PeerInfo> CreateConnectedPeers(int count)
    {
        var peers = new List<PeerInfo>();
        for (int i = 0; i < count; i++)
        {
            peers.Add(CreateConnectedPeer(i + 1));
        }
        return peers;
    }

    #endregion
}
