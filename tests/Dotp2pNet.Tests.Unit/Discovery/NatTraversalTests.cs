using Dotp2pNet.Discovery.NatTraversal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dotp2pNet.Tests.Unit.Discovery;

/// <summary>
/// Tests for NAT traversal functionality.
/// </summary>
public class NatTraversalTests
{
    private readonly ILogger<NatTraversal> _logger = NullLogger<NatTraversal>.Instance;

    [Fact]
    public async Task GetExternalIpAsync_ShouldReturnValidIpAddress()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);

        // Act
        var externalIp = await natTraversal.GetExternalIpAsync();

        // Assert
        Assert.NotNull(externalIp);
        Assert.NotEqual(System.Net.IPAddress.None, externalIp);
        Assert.NotEqual(System.Net.IPAddress.Any, externalIp);
        
        // External IP should be a valid IPv4 address
        Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, externalIp.AddressFamily);
    }

    [Fact]
    public async Task GetExternalIpAsync_WithCancellation_ShouldReturnNull()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await natTraversal.GetExternalIpAsync(cts.Token);

        // Assert
        // Should return null when cancelled, not throw
        Assert.Null(result);
    }

    [Fact]
    public async Task TryUpnpPortMappingAsync_ShouldHandleNoUpnpRouter()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);

        // Act
        // This will attempt to discover a UPnP router
        // In most test environments, this will return false (no UPnP router)
        // But the method should handle this gracefully without throwing
        var result = await natTraversal.TryUpnpPortMappingAsync(6881, 6881);

        // Assert
        // We can't assert true/false definitively since it depends on the network
        // But we can assert that the method completes without throwing
        Assert.IsType<bool>(result);
    }

    [Fact]
    public async Task TryUpnpPortMappingAsync_WithCancellation_ShouldReturnFalseGracefully()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        // The implementation catches OperationCanceledException and returns false gracefully
        var result = await natTraversal.TryUpnpPortMappingAsync(6881, 6881, cts.Token);

        // Assert
        // Should return false when cancelled, not throw
        Assert.False(result);
    }

    [Fact]
    public async Task DeletePortMappingAsync_ShouldHandleNoUpnpRouter()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);

        // Act
        // This will attempt to discover a UPnP router and delete a mapping
        // In most test environments, this will return false (no UPnP router)
        var result = await natTraversal.DeletePortMappingAsync(6881);

        // Assert
        // We can't assert true/false definitively since it depends on the network
        // But we can assert that the method completes without throwing
        Assert.IsType<bool>(result);
    }

    [Fact]
    public async Task TryUdpHolePunchingAsync_WithInvalidPeer_ShouldReturnFalse()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);
        var invalidPeer = new Core.Models.PeerInfo
        {
            PeerId = new byte[10], // Invalid: should be 20 bytes
            IpAddress = System.Net.IPAddress.Parse("1.2.3.4"),
            Port = 6881
        };

        // Act
        var result = await natTraversal.TryUdpHolePunchingAsync(invalidPeer);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryUdpHolePunchingAsync_WithValidPeer_ShouldAttemptHolePunching()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);
        var validPeer = new Core.Models.PeerInfo
        {
            PeerId = new byte[20], // Valid peer ID
            IpAddress = System.Net.IPAddress.Parse("8.8.8.8"), // Google DNS (won't respond to our protocol)
            Port = 6881
        };

        // Act
        // This will attempt hole punching but will fail because:
        // 1. The remote peer (8.8.8.8) won't respond to our custom protocol
        // 2. There's no coordination/rendezvous server
        // But the method should handle this gracefully
        var result = await natTraversal.TryUdpHolePunchingAsync(validPeer);

        // Assert
        // Should return false since the remote peer won't respond
        Assert.False(result);
    }

    [Fact]
    public async Task TryUdpHolePunchingAsync_WithCancellation_ShouldReturnFalseGracefully()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);
        var validPeer = new Core.Models.PeerInfo
        {
            PeerId = new byte[20],
            IpAddress = System.Net.IPAddress.Parse("1.2.3.4"),
            Port = 6881
        };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        // The implementation catches OperationCanceledException and returns false gracefully
        var result = await natTraversal.TryUdpHolePunchingAsync(validPeer, cts.Token);

        // Assert
        // Should return false when cancelled, not throw
        Assert.False(result);
    }
}
