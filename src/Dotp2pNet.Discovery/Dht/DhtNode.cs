using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Implements a Kademlia DHT node for decentralized peer discovery.
/// </summary>
/// <remarks>
/// The DHT node maintains a routing table of known nodes organized by XOR distance.
/// It responds to RPC queries (ping, find_node, get_peers, announce_peer) and
/// performs iterative lookups to find peers sharing files.
/// </remarks>
public class DhtNode : IDhtNode
{
    private readonly ILogger<DhtNode> _logger;
    private readonly DhtMessageCodec _codec = new();
    private readonly RoutingTable _routingTable;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DhtMessage>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, List<(IPAddress, int)>> _peerStorage = new();
    private readonly ConcurrentDictionary<string, byte[]> _tokens = new();
    
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private int _port;
    private bool _isRunning;

    /// <summary>
    /// Gets the unique 160-bit identifier for this DHT node.
    /// </summary>
    public byte[] NodeId { get; }

    /// <summary>
    /// Gets a value indicating whether the DHT node is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the port this DHT node is listening on.
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// Initializes a new instance of the <see cref="DhtNode"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public DhtNode(ILogger<DhtNode> logger)
    {
        _logger = logger;
        
        // Generate cryptographically random 160-bit node ID
        NodeId = GenerateNodeId();
        _routingTable = new RoutingTable(NodeId);

        _logger.LogInformation("DHT node created with ID: {NodeId}", 
            BitConverter.ToString(NodeId[..4]).Replace("-", "").ToLower());
    }

    /// <summary>
    /// Starts the DHT node and begins listening for incoming requests.
    /// </summary>
    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("DHT node is already running");

        _port = port;
        _udpClient = new UdpClient(port);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _isRunning = true;

        _logger.LogInformation(
            "DHT node started on port {Port}, NodeId={NodeId}",
            port,
            BitConverter.ToString(NodeId[..8]).Replace("-", "").ToLower());
        
        _logger.LogDebug(
            "DHT node initialization: Port={Port}, IsRunning={IsRunning}",
            port,
            _isRunning);

        // Start receiving messages
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the DHT node and closes all connections.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("Stopping DHT node...");

        _isRunning = false;
        _cts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _udpClient?.Close();
        _udpClient?.Dispose();

        _logger.LogInformation("DHT node stopped");
    }

    /// <summary>
    /// Bootstraps the DHT node by connecting to known bootstrap nodes.
    /// </summary>
    public async Task BootstrapAsync(List<PeerInfo> bootstrapNodes, CancellationToken ct = default)
    {
        if (!_isRunning)
            throw new InvalidOperationException("DHT node is not running");

        _logger.LogInformation(
            "Bootstrapping DHT with {Count} bootstrap nodes",
            bootstrapNodes.Count);
        
        _logger.LogDebug(
            "DHT bootstrap details: BootstrapNodeCount={Count}, NodeId={NodeId}",
            bootstrapNodes.Count,
            BitConverter.ToString(NodeId[..4]).Replace("-", "").ToLower());

        // Add bootstrap nodes to routing table
        foreach (var peer in bootstrapNodes)
        {
            var node = new DhtNodeInfo
            {
                NodeId = peer.PeerId,
                IpAddress = peer.IpAddress,
                Port = peer.Port
            };
            _routingTable.AddNode(node);
        }

        // Perform find_node lookup for our own ID to populate routing table
        try
        {
            await FindNodeAsync(NodeId, ct);
            _logger.LogInformation("DHT bootstrap complete. Routing table size: {Size}", 
                _routingTable.TotalNodes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DHT bootstrap encountered errors but may have partially succeeded");
        }
    }

    /// <summary>
    /// Finds peers that are sharing a specific file (info hash).
    /// </summary>
    public async Task<List<PeerInfo>> FindPeersAsync(byte[] infoHash, CancellationToken ct = default)
    {
        if (!_isRunning)
            throw new InvalidOperationException("DHT node is not running");

        if (infoHash.Length != 20)
            throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));

        _logger.LogDebug("Finding peers for info hash: {InfoHash}", 
            BitConverter.ToString(infoHash[..4]).Replace("-", "").ToLower());

        var peers = new List<PeerInfo>();
        var queriedNodes = new HashSet<string>();
        var closestNodes = _routingTable.FindClosestNodes(infoHash, KBucket.K);

        // Iterative lookup
        for (int iteration = 0; iteration < 10 && closestNodes.Count > 0; iteration++)
        {
            var nodesToQuery = closestNodes
                .Where(n => !queriedNodes.Contains(Convert.ToBase64String(n.NodeId)))
                .Take(3) // Alpha = 3 (parallel queries)
                .ToList();

            if (nodesToQuery.Count == 0)
                break;

            var tasks = nodesToQuery.Select(async node =>
            {
                queriedNodes.Add(Convert.ToBase64String(node.NodeId));
                
                try
                {
                    var response = await GetPeersAsync(node, infoHash, ct);
                    return (node, response);
                }
                catch
                {
                    _routingTable.MarkNodeFailed(node.NodeId);
                    return (node, (DhtResponse?)null);
                }
            });

            var results = await Task.WhenAll(tasks);

            foreach (var (node, response) in results)
            {
                if (response == null)
                    continue;

                _routingTable.MarkNodeSeen(node.NodeId);

                // Add returned peers
                if (response.Peers != null)
                {
                    foreach (var (address, port) in response.Peers)
                    {
                        peers.Add(new PeerInfo
                        {
                            PeerId = new byte[20], // We don't know peer ID yet
                            IpAddress = address,
                            Port = port
                        });
                    }
                }

                // Add returned nodes to routing table and closest nodes list
                if (response.Nodes != null)
                {
                    foreach (var returnedNode in response.Nodes)
                    {
                        _routingTable.AddNode(returnedNode);
                        if (!queriedNodes.Contains(Convert.ToBase64String(returnedNode.NodeId)))
                        {
                            closestNodes.Add(returnedNode);
                        }
                    }
                }
            }

            // Re-sort closest nodes by distance
            closestNodes = closestNodes
                .OrderBy(n => XorDistance.Calculate(infoHash, n.NodeId))
                .Take(KBucket.K)
                .ToList();
        }

        var distinctPeers = peers.DistinctBy(p => p.Endpoint).ToList();
        
        _logger.LogInformation(
            "Found {Count} peers for info hash {InfoHash} (after deduplication: {DistinctCount})",
            peers.Count,
            Convert.ToHexString(infoHash[..4]),
            distinctPeers.Count);
        
        _logger.LogDebug(
            "DHT peer discovery details: InfoHash={InfoHash}, TotalPeers={TotalPeers}, " +
            "DistinctPeers={DistinctPeers}, QueriedNodes={QueriedNodes}",
            Convert.ToHexString(infoHash[..4]),
            peers.Count,
            distinctPeers.Count,
            queriedNodes.Count);
        
        return distinctPeers;
    }

    /// <summary>
    /// Announces that this peer is sharing a file (info hash).
    /// </summary>
    public async Task AnnouncePeerAsync(byte[] infoHash, int port, CancellationToken ct = default)
    {
        if (!_isRunning)
            throw new InvalidOperationException("DHT node is not running");

        if (infoHash.Length != 20)
            throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));

        _logger.LogDebug("Announcing peer for info hash: {InfoHash} on port {Port}", 
            BitConverter.ToString(infoHash[..4]).Replace("-", "").ToLower(), port);

        // Find closest nodes to the info hash
        var closestNodes = _routingTable.FindClosestNodes(infoHash, KBucket.K);

        // Get peers from each node to obtain tokens
        var announceTargets = new List<(DhtNodeInfo node, byte[] token)>();
        
        foreach (var node in closestNodes)
        {
            try
            {
                var response = await GetPeersAsync(node, infoHash, ct);
                if (response?.Token != null)
                {
                    announceTargets.Add((node, response.Token));
                }
            }
            catch
            {
                _routingTable.MarkNodeFailed(node.NodeId);
            }
        }

        // Announce to nodes that gave us tokens
        foreach (var (node, token) in announceTargets)
        {
            try
            {
                await AnnouncePeerToNodeAsync(node, infoHash, port, token, ct);
                _logger.LogDebug("Announced to node {Node}", node);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to announce to node {Node}", node);
            }
        }

        _logger.LogInformation("Announced peer to {Count} nodes", announceTargets.Count);
    }

    /// <summary>
    /// Pings a remote DHT node to check if it's alive.
    /// </summary>
    public async Task<bool> PingAsync(byte[] nodeId, IPAddress address, int port, CancellationToken ct = default)
    {
        if (!_isRunning)
            throw new InvalidOperationException("DHT node is not running");

        try
        {
            var query = new DhtQuery
            {
                TransactionId = GenerateTransactionId(),
                Method = DhtMethod.Ping,
                QueryingNodeId = NodeId
            };

            var response = await SendQueryAsync(query, address, port, ct);
            
            if (response is DhtResponse)
            {
                var node = new DhtNodeInfo
                {
                    NodeId = nodeId,
                    IpAddress = address,
                    Port = port
                };
                _routingTable.AddNode(node);
                _routingTable.MarkNodeSeen(nodeId);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the closest nodes to a target ID.
    /// </summary>
    public async Task<List<PeerInfo>> FindNodeAsync(byte[] targetId, CancellationToken ct = default)
    {
        if (!_isRunning)
            throw new InvalidOperationException("DHT node is not running");

        if (targetId.Length != 20)
            throw new ArgumentException("Target ID must be 20 bytes", nameof(targetId));

        var foundNodes = new List<PeerInfo>();
        var queriedNodes = new HashSet<string>();
        var closestNodes = _routingTable.FindClosestNodes(targetId, KBucket.K);

        // Iterative lookup
        for (int iteration = 0; iteration < 10 && closestNodes.Count > 0; iteration++)
        {
            var nodesToQuery = closestNodes
                .Where(n => !queriedNodes.Contains(Convert.ToBase64String(n.NodeId)))
                .Take(3)
                .ToList();

            if (nodesToQuery.Count == 0)
                break;

            var tasks = nodesToQuery.Select(async node =>
            {
                queriedNodes.Add(Convert.ToBase64String(node.NodeId));
                
                try
                {
                    var response = await FindNodeFromNodeAsync(node, targetId, ct);
                    return (node, response);
                }
                catch
                {
                    _routingTable.MarkNodeFailed(node.NodeId);
                    return (node, (DhtResponse?)null);
                }
            });

            var results = await Task.WhenAll(tasks);

            foreach (var (node, response) in results)
            {
                if (response?.Nodes == null)
                    continue;

                _routingTable.MarkNodeSeen(node.NodeId);

                foreach (var returnedNode in response.Nodes)
                {
                    _routingTable.AddNode(returnedNode);
                    foundNodes.Add(new PeerInfo
                    {
                        PeerId = returnedNode.NodeId,
                        IpAddress = returnedNode.IpAddress,
                        Port = returnedNode.Port
                    });

                    if (!queriedNodes.Contains(Convert.ToBase64String(returnedNode.NodeId)))
                    {
                        closestNodes.Add(returnedNode);
                    }
                }
            }

            closestNodes = closestNodes
                .OrderBy(n => XorDistance.Calculate(targetId, n.NodeId))
                .Take(KBucket.K)
                .ToList();
        }

        return foundNodes.DistinctBy(p => Convert.ToBase64String(p.PeerId)).ToList();
    }

    /// <summary>
    /// Gets the current number of nodes in the routing table.
    /// </summary>
    public int GetRoutingTableSize()
    {
        return _routingTable.TotalNodes;
    }

    private async Task<DhtResponse?> GetPeersAsync(DhtNodeInfo node, byte[] infoHash, CancellationToken ct)
    {
        var query = new DhtQuery
        {
            TransactionId = GenerateTransactionId(),
            Method = DhtMethod.GetPeers,
            QueryingNodeId = NodeId,
            InfoHash = infoHash
        };

        var response = await SendQueryAsync(query, node.IpAddress, node.Port, ct);
        return response as DhtResponse;
    }

    private async Task<DhtResponse?> FindNodeFromNodeAsync(DhtNodeInfo node, byte[] targetId, CancellationToken ct)
    {
        var query = new DhtQuery
        {
            TransactionId = GenerateTransactionId(),
            Method = DhtMethod.FindNode,
            QueryingNodeId = NodeId,
            TargetId = targetId
        };

        var response = await SendQueryAsync(query, node.IpAddress, node.Port, ct);
        return response as DhtResponse;
    }

    private async Task AnnouncePeerToNodeAsync(DhtNodeInfo node, byte[] infoHash, int port, byte[] token, CancellationToken ct)
    {
        var query = new DhtQuery
        {
            TransactionId = GenerateTransactionId(),
            Method = DhtMethod.AnnouncePeer,
            QueryingNodeId = NodeId,
            InfoHash = infoHash,
            Port = port,
            Token = token
        };

        await SendQueryAsync(query, node.IpAddress, node.Port, ct);
    }

    private async Task<DhtMessage?> SendQueryAsync(DhtQuery query, IPAddress address, int port, CancellationToken ct)
    {
        if (_udpClient == null)
            throw new InvalidOperationException("UDP client is not initialized");

        var transactionId = Convert.ToBase64String(query.TransactionId);
        var tcs = new TaskCompletionSource<DhtMessage>();
        _pendingRequests[transactionId] = tcs;

        try
        {
            var data = _codec.Encode(query);
            await _udpClient.SendAsync(data, data.Length, new IPEndPoint(address, port));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            
            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            throw new TimeoutException("DHT query timed out");
        }
        finally
        {
            _pendingRequests.TryRemove(transactionId, out _);
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                _ = Task.Run(() => HandleMessage(result.Buffer, result.RemoteEndPoint), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving DHT message");
            }
        }
    }

    private void HandleMessage(byte[] data, IPEndPoint remoteEndPoint)
    {
        try
        {
            var message = _codec.Decode(data);
            if (message == null)
                return;

            var transactionId = Convert.ToBase64String(message.TransactionId);

            if (message is DhtQuery query)
            {
                HandleQuery(query, remoteEndPoint);
            }
            else if (message is DhtResponse || message is DhtError)
            {
                if (_pendingRequests.TryGetValue(transactionId, out var tcs))
                {
                    tcs.TrySetResult(message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling DHT message from {Endpoint}", remoteEndPoint);
        }
    }

    private void HandleQuery(DhtQuery query, IPEndPoint remoteEndPoint)
    {
        // Add querying node to routing table
        var node = new DhtNodeInfo
        {
            NodeId = query.QueryingNodeId,
            IpAddress = remoteEndPoint.Address,
            Port = remoteEndPoint.Port
        };
        _routingTable.AddNode(node);

        DhtMessage response = query.Method switch
        {
            DhtMethod.Ping => HandlePing(query),
            DhtMethod.FindNode => HandleFindNode(query),
            DhtMethod.GetPeers => HandleGetPeers(query, remoteEndPoint),
            DhtMethod.AnnouncePeer => HandleAnnouncePeer(query, remoteEndPoint),
            _ => new DhtError
            {
                TransactionId = query.TransactionId,
                ErrorCode = DhtErrorCode.MethodUnknown,
                ErrorMessage = "Unknown method"
            }
        };

        SendResponse(response, remoteEndPoint);
    }

    private DhtResponse HandlePing(DhtQuery query)
    {
        return new DhtResponse
        {
            TransactionId = query.TransactionId,
            RespondingNodeId = NodeId
        };
    }

    private DhtResponse HandleFindNode(DhtQuery query)
    {
        var closestNodes = query.TargetId != null
            ? _routingTable.FindClosestNodes(query.TargetId, KBucket.K)
            : new List<DhtNodeInfo>();

        return new DhtResponse
        {
            TransactionId = query.TransactionId,
            RespondingNodeId = NodeId,
            Nodes = closestNodes
        };
    }

    private DhtResponse HandleGetPeers(DhtQuery query, IPEndPoint remoteEndPoint)
    {
        var response = new DhtResponse
        {
            TransactionId = query.TransactionId,
            RespondingNodeId = NodeId,
            Token = GenerateToken(remoteEndPoint)
        };

        if (query.InfoHash != null)
        {
            var infoHashKey = Convert.ToBase64String(query.InfoHash);
            
            if (_peerStorage.TryGetValue(infoHashKey, out var peers) && peers.Count > 0)
            {
                response.Peers = peers;
            }
            else
            {
                response.Nodes = _routingTable.FindClosestNodes(query.InfoHash, KBucket.K);
            }
        }

        return response;
    }

    private DhtResponse HandleAnnouncePeer(DhtQuery query, IPEndPoint remoteEndPoint)
    {
        if (query.InfoHash != null && query.Port.HasValue && query.Token != null)
        {
            // Verify token
            var expectedToken = GenerateToken(remoteEndPoint);
            if (query.Token.SequenceEqual(expectedToken))
            {
                var infoHashKey = Convert.ToBase64String(query.InfoHash);
                var peerList = _peerStorage.GetOrAdd(infoHashKey, _ => new List<(IPAddress, int)>());
                
                lock (peerList)
                {
                    var peer = (remoteEndPoint.Address, query.Port.Value);
                    if (!peerList.Contains(peer))
                    {
                        peerList.Add(peer);
                        _logger.LogDebug("Stored peer {Peer} for info hash {InfoHash}", 
                            peer, BitConverter.ToString(query.InfoHash[..4]).Replace("-", "").ToLower());
                    }
                }
            }
        }

        return new DhtResponse
        {
            TransactionId = query.TransactionId,
            RespondingNodeId = NodeId
        };
    }

    private void SendResponse(DhtMessage response, IPEndPoint remoteEndPoint)
    {
        try
        {
            var data = _codec.Encode(response);
            _udpClient?.Send(data, data.Length, remoteEndPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending DHT response to {Endpoint}", remoteEndPoint);
        }
    }

    private static byte[] GenerateNodeId()
    {
        return RandomNumberGenerator.GetBytes(20);
    }

    private static byte[] GenerateTransactionId()
    {
        return RandomNumberGenerator.GetBytes(2);
    }

    private byte[] GenerateToken(IPEndPoint endpoint)
    {
        // Simple token generation based on IP and a secret
        // In production, this should rotate periodically
        var data = endpoint.Address.GetAddressBytes().Concat(NodeId).ToArray();
        return SHA256.HashData(data)[..8];
    }
}
