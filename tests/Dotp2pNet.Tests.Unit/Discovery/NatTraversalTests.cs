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
    public async Task GetExternalIpAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // TaskCanceledException is a subclass of OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await natTraversal.GetExternalIpAsync(cts.Token);
        });
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
    public async Task TryUdpHolePunchingAsync_ShouldReturnFalse_WhenNotImplemented()
    {
        // Arrange
        var natTraversal = new NatTraversal(_logger);
        var remotePeer = new Core.Models.PeerInfo
        {
            PeerId = new byte[20],
            IpAddress = System.Net.IPAddress.Parse("1.2.3.4"),
            Port = 6881
        };

        // Act
        var result = await natTraversal.TryUdpHolePunchingAsync(remotePeer);

        // Assert
        Assert.False(result);
    }
}
