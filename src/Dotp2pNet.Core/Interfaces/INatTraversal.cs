using System.Net;

namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Provides NAT (Network Address Translation) traversal capabilities for P2P connectivity.
/// </summary>
/// <remarks>
/// NAT traversal is essential for P2P networks because most peers are behind routers
/// that perform NAT, making direct connections difficult. This interface provides
/// multiple strategies:
/// 
/// 1. STUN (Session Traversal Utilities for NAT): Discovers external IP address
/// 2. UPnP (Universal Plug and Play): Automatic port forwarding
/// 3. UDP Hole Punching: Establishes direct connections through NAT
/// 
/// These techniques allow peers behind NAT to communicate directly without
/// requiring manual port forwarding or relay servers.
/// </remarks>
public interface INatTraversal
{
    /// <summary>
    /// Discovers the peer's external (public) IP address using STUN protocol.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The external IP address, or null if discovery fails.</returns>
    /// <remarks>
    /// STUN (RFC 5389) works by sending a binding request to a public STUN server.
    /// The server responds with the source IP address and port it observed,
    /// revealing the peer's external address after NAT translation.
    /// 
    /// This is useful for:
    /// - Determining if the peer is behind NAT
    /// - Announcing the correct address to trackers and DHT
    /// - Coordinating hole punching attempts
    /// </remarks>
    Task<IPAddress?> GetExternalIpAsync(CancellationToken ct = default);

    /// <summary>
    /// Attempts to configure automatic port forwarding using UPnP.
    /// </summary>
    /// <param name="internalPort">The local port to forward.</param>
    /// <param name="externalPort">The external port to map to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if port mapping was successful; otherwise, false.</returns>
    /// <remarks>
    /// UPnP allows applications to automatically configure port forwarding on
    /// compatible routers. This eliminates the need for manual configuration.
    /// 
    /// Process:
    /// 1. Discover UPnP-enabled router using SSDP (Simple Service Discovery Protocol)
    /// 2. Send AddPortMapping request via UPnP protocol
    /// 3. Router creates forwarding rule: external_port -> internal_ip:internal_port
    /// 
    /// Not all routers support UPnP, and some users disable it for security reasons.
    /// </remarks>
    Task<bool> TryUpnpPortMappingAsync(int internalPort, int externalPort, CancellationToken ct = default);

    /// <summary>
    /// Attempts to establish a direct connection through NAT using UDP hole punching.
    /// </summary>
    /// <param name="remotePeer">Information about the remote peer to connect to.</param>
    /// <param name="ct">Cancellation token.</param>
    ///  /// <returns>Trpunching succeeded; otherwise, false.</returns>
    /// <remarks>
    /// UDP hole punching exploits NAT behavior to create direct peer-to-peer connections.
    /// 
    /// Process:
    /// 1. Both peers send UDP packets to each other's external addresses simultaneously
    /// 2. These packets create temporary "holes" in the NAT mapping
    /// 3. Subsequent packets can traverse the NAT in both directions
    /// 
    /// This technique works with many (but not all) types of NAT. If it fails,
    /// the application should fall back to relay-based communication.
    /// 
    /// Requires coordination through a third party (tracker or DHT) to exchange
    /// external addresses and synchronize the simultaneous send.
    /// </remarks>
    Task<bool> TryUdpHolePunchingAsync(Models.PeerInfo remotePeer, CancellationToken ct = default);
}
