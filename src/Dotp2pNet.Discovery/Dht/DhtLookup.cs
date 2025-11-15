using System.Collections.Concurrent;
using System.Net;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Performs iterative lookups in the DHT network to find nodes or peers.
/// </summary>
/// <remarks>
/// Implements the Kademlia iterative lookup algorithm with parallel queries.
/// The algorithm maintains a list of the K closest nodes found so far and
/// iteratively queries them to converge on the target. Uses alpha=3 parallel
/// queries for faster convergence while avoiding overwhelming the network.
/// </remarks>
public class DhtLookup
{
    private readonly ILogger<DhtLookup> _logger;
    private readonly DhtNode _dhtNode;
    private readonly DhtMessageCodec _codec = new();
    
    // Kademlia parameters
    private const int K = KBucket.K; // Number of closest nodes to maintain (8)
    private const int Alpha = 3; // Number of parallel queries
    private const int MaxIterations = 10; // Maximum lookup iterations
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="DhtLookup"/> class.
    /// </summary>
    /// <param name="dhtNode">The DHT node to use for queries.</param>
    /// <param name="logger">The logger instance.</param>
    public DhtLookup(DhtNode dhtNode, ILogger<DhtLookup> logger)
    {
        _dhtNode = dhtNode;
        _logger = logger;
    }

    /// <summary>
    /// Finds peers sharing a specific file using iterative DHT lookup.
    /// </summary>
    /// <param name="infoHash">The 20-byte info hash of the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of peers found.</returns>
    /// // <remarks>
    /// This met   //mplements the iterative closest-node algorithm:
    /// 1. Start with K closest nodes from our routing table
    /// 2. Query alpha (3) nodes in parallel
    /// 3. Add returned nodes to our candidate list
    /// 4. Re-sort by distance and query the next closest unqueried nodes
    /// 5. Terminate when K closest nodes have been queried or no closer nodes are found
    /// 6. Collect peers from nodes that have them
    /// </remarks>
    public async Task<List<PeerInfo>> FindPeersAsync(byte[] infoHash, CancellationToken ct = default)
    {
        if (!_dhtNode.IsRunning)
            throw new InvalidOperationException("DHT node is not running");

        if (infoHash.Length != 20)
            throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));

        _logger.LogDebug("Starting iterative lookup for info hash: {InfoHash}", 
            BitConverter.ToString(infoHash[..4]).Replace("-", "").ToLower());

        var lookupState = new LookupState(infoHash);
        var peers = new ConcurrentBag<PeerInfo>();

        // Initialize with K closest nodes from our routing table
        var initialNodes = GetInitialNodes(infoHash);
        foreach (var node in initialNodes)
        {
            lookupState.AddCandidate(node);
        }

        if (lookupState.CandidateCount == 0)
        {
            _logger.LogWarning("No initial nodes available for lookup. DHT may not be bootstrapped.");
            return new List<PeerInfo>();
        }

        _logger.LogDebug("Starting with {Count} initial candidates", lookupState.CandidateCount);

        // Iterative lookup loop
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            // Get next alpha nodes to query
            var nodesToQuery = lookupState.GetNextNodesToQuery(Alpha);
            
            if (nodesToQuery.Count == 0)
            {
                _logger.LogDebug("No more nodes to query. Lookup complete after {Iterations} iterations.", 
                    iteration);
                break;
            }

            _logger.LogDebug("Iteration {Iteration}: Querying {Count} nodes in parallel", 
                iteration + 1, nodesToQuery.Count);

            // Query nodes in parallel
            var queryTasks = nodesToQuery.Select(node => 
                QueryNodeForPeersAsync(node, infoHash, lookupState, peers, ct));

            await Task.WhenAll(queryTasks);

            // Check if we've converged (K closest nodes have been queried)
            if (lookupState.HasConverged(K))
            {
                _logger.LogDebug("Lookup converged after {Iterations} iterations. " +
                    "K closest nodes have been queried.", iteration + 1);
                break;
            }
        }

        var peerList = peers.ToList();
        _logger.LogInformation("Iterative lookup complete. Found {PeerCount} peers from {QueriedCount} nodes queried.",
            peerList.Count, lookupState.QueriedCount);

        return peerList.DistinctBy(p => p.Endpoint).ToList();
    }

    /// <summary>
    ///   /// Finds the closess to a target ID using iterative DHT lookup.
    /// </summary>
    /// <param name="targetId">The 20-byte target ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of closest nodes found.</returns>
    public async Task<List<DhtNodeInfo>> FindClosestNodesAsync(byte[] targetId, CancellationToken ct = default)
    {
        if (!_dhtNode.IsRunning)
            throw new InvalidOperationException("DHT node is not running");

        if (targetId.Length != 20)
            throw new ArgumentException("Target ID must be 20 bytes", nameof(targetId));

        _logger.LogDebug("Starting iterative node lookup for target: {TargetId}", 
            BitConverter.ToString(targetId[..4]).Replace("-", "").ToLower());

        var lookupState = new LookupState(targetId);

        // Initialize with K closest nodes from our routing table
        var initialNodes = GetInitialNodes(targetId);
        foreach (var node in initialNodes)
        {
            lookupState.AddCandidate(node);
        }

        if (lookupState.CandidateCount == 0)
        {
            _logger.LogWarning("No initial nodes available for lookup.");
            return new List<DhtNodeInfo>();
        }

        // Iterative lookup loop
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            var nodesToQuery = lookupState.GetNextNodesToQuery(Alpha);
            
            if (nodesToQuery.Count == 0)
                break;

            _logger.LogDebug("Iteration {Iteration}: Querying {Count} nodes", 
                iteration + 1, nodesToQuery.Count);

            // Query nodes in parallel
            var queryTasks = nodesToQuery.Select(node => 
                QueryNodeForNodesAsync(node, targetId, lookupState, ct));

            await Task.WhenAll(queryTasks);

            if (lookupState.HasConverged(K))
            {
                _logger.LogDebug("Node lookup converged after {Iterations} iterations.", 
                    iteration + 1);
                break;
            }
        }

        var closestNodes = lookupState.GetClosestNodes(K);
        _logger.LogInformation("Node lookup complete. Found {Count} closest nodes from {QueriedCount} queried.",
            closestNodes.Count, lookupState.QueriedCount);

        return closestNodes;
    }

    /// <summary>
    /// Queries a single node for peers and updates the lookup state.
    /// </summary>
    private async Task QueryNodeForPeersAsync(
        DhtNodeInfo node,
        byte[] infoHash,
        LookupState lookupState,
        ConcurrentBag<PeerInfo> peers,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(QueryTimeout);

            var response = await GetPeersFromNodeAsync(node, infoHash, timeoutCts.Token);

            if (response != null)
            {
                lookupState.MarkNodeResponded(node);

                // Add any peers returned
                if (response.Peers != null && response.Peers.Count > 0)
                {
                    _logger.LogDebug("Node {Node} returned {Count} peers", 
                        node, response.Peers.Count);

                    foreach (var (address, port) in response.Peers)
                    {
                        peers.Add(new PeerInfo
                        {
                            PeerId = new byte[20], // Unknown at this point
                            IpAddress = address,
                            Port = port
                        });
                    }
                }

                // Add any nodes returned
                if (response.Nodes != null && response.Nodes.Count > 0)
                {
                    _logger.LogDebug("Node {Node} returned {Count} nodes", 
                        node, response.Nodes.Count);

                    foreach (var returnedNode in response.Nodes)
                    {
                        lookupState.AddCandidate(returnedNode);
                    }
                }
            }
            else
            {
                lookupState.MarkNodeFailed(node);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Query to node {Node} timed out", node);
            lookupState.MarkNodeFailed(node);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Query to node {Node} failed", node);
            lookupState.MarkNodeFailed(node);
        }
    }

    /// <summary>
    /// Queries a single node for closest nodes and updates the lookup state.
    /// </summary>
    private async Task QueryNodeForNodesAsync(
        DhtNodeInfo node,
        byte[] targetId,
        LookupState lookupState,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(QueryTimeout);

            var response = await FindNodeFromNodeAsync(node, targetId, timeoutCts.Token);

            if (response != null)
            {
                lookupState.MarkNodeResponded(node);

                if (response.Nodes != null && response.Nodes.Count > 0)
                {
                    _logger.LogDebug("Node {Node} returned {Count} nodes", 
                        node, response.Nodes.Count);

                    foreach (var returnedNode in response.Nodes)
                    {
                        lookupState.AddCandidate(returnedNode);
                    }
                }
            }
            else
            {
                lookupState.MarkNodeFailed(node);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Query to node {Node} timed out", node);
            lookupState.MarkNodeFailed(node);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Query to node {Node} failed", node);
            lookupState.MarkNodeFailed(node);
        }
    }

    /// <summary>
    /// Gets initial candidate nodes from the routing table.
    /// </summary>
    private List<DhtNodeInfo> GetInitialNodes(byte[] targetId)
    {
        // Access the routing table through reflection or add a public method
        // For now, we'll use the existing FindNodeAsync which does a similar lookup
        // In a real implementation, we'd want direct access to the routing table
        
        var routingTableField = typeof(DhtNode).GetField("_routingTable", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (routingTableField?.GetValue(_dhtNode) is RoutingTable routingTable)
        {
            return routingTable.FindClosestNodes(targetId, K);
        }

        return new List<DhtNodeInfo>();
    }

    /// <summary>
    /// Sends a get_peers query to a specific node.
    /// </summary>
    private async Task<DhtResponse?> GetPeersFromNodeAsync(
        DhtNodeInfo node,
        byte[] infoHash,
        CancellationToken ct)
    {
        // Use reflection to access the private GetPeersAsync method
        var method = typeof(DhtNode).GetMethod("GetPeersAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method != null)
        {
            var task = method.Invoke(_dhtNode, new object[] { node, infoHash, ct }) as Task<DhtResponse?>;
            return task != null ? await task : null;
        }

        return null;
    }

    /// <summary>
    /// Sends a find_node query to a specific node.
    /// </summary>
    private async Task<DhtResponse?> FindNodeFromNodeAsync(
        DhtNodeInfo node,
        byte[] targetId,
        CancellationToken ct)
    {
        // Use reflection to access the private FindNodeFromNodeAsync method
        var method = typeof(DhtNode).GetMethod("FindNodeFromNodeAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method != null)
        {
            var task = method.Invoke(_dhtNode, new object[] { node, targetId, ct }) as Task<DhtResponse?>;
            return task != null ? await task : null;
        }

        return null;
    }

    /// <summary>
    /// Maintains state during an iterative lookup.
    /// </summary>
    private class LookupState
    {
        private readonly byte[] _targetId;
        private readonly ConcurrentDictionary<string, DhtNodeInfo> _candidates = new();
        private readonly ConcurrentDictionary<string, bool> _queriedNodes = new();
        private readonly ConcurrentDictionary<string, bool> _respondedNodes = new();
        private readonly object _lock = new();

        public int CandidateCount => _candidates.Count;
        public int QueriedCount => _queriedNodes.Count;

        public LookupState(byte[] targetId)
        {
            _targetId = targetId;
        }

        /// <summary>
        /// Adds a candidate node to the lookup state.
        /// </summary>
        public void AddCandidate(DhtNodeInfo node)
        {
            var nodeKey = GetNodeKey(node.NodeId);
            _candidates.TryAdd(nodeKey, node);
        }

        /// <summary>
        /// Gets the next batch of nodes to query.
        /// </summary>
        public List<DhtNodeInfo> GetNextNodesToQuery(int count)
        {
            lock (_lock)
            {
                return _candidates.Values
                    .Where(n => !_queriedNodes.ContainsKey(GetNodeKey(n.NodeId)))
                    .OrderBy(n => XorDistance.Calculate(_targetId, n.NodeId))
                    .Take(count)
                    .ToList();
            }
        }

        /// <summary>
        /// Marks a node as queried.
        /// </summary>
        public void MarkNodeQueried(DhtNodeInfo node)
        {
            var nodeKey = GetNodeKey(node.NodeId);
            _queriedNodes.TryAdd(nodeKey, true);
        }

        /// <summary>
        /// Marks a node as having responded successfully.
        /// </summary>
        public void MarkNodeResponded(DhtNodeInfo node)
        {
            var nodeKey = GetNodeKey(node.NodeId);
            _queriedNodes.TryAdd(nodeKey, true);
            _respondedNodes.TryAdd(nodeKey, true);
        }

        /// <summary>
        /// Marks a node as having failed to respond.
        /// </summary>
        public void MarkNodeFailed(DhtNodeInfo node)
        {
            var nodeKey = GetNodeKey(node.NodeId);
            _queriedNodes.TryAdd(nodeKey, true);
        }

        /// <summary>
        /// Checks if the lookup has converged.
        /// </summary>
        /// <remarks>
        /// Convergence occurs when the K closest nodes have all been queried.
        /// This means we've exhausted the closest nodes and won't find anything closer.
        /// </remarks>
        public bool HasConverged(int k)
        {
            lock (_lock)
            {
                var closestK = _candidates.Values
                    .OrderBy(n => XorDistance.Calculate(_targetId, n.NodeId))
                    .Take(k)
                    .ToList();

                // Check if all K closest nodes have been queried
                return closestK.All(n => _queriedNodes.ContainsKey(GetNodeKey(n.NodeId)));
            }
        }

        /// <summary>
        ///         ///closest nodes found so far.
        /// </summary>
        public List<DhtNodeInfo> GetClosestNodes(int k)
        {
            lock (_lock)
            {
                return _candidates.Values
                    .OrderBy(n => XorDistance.Calculate(_targetId, n.NodeId))
                    .Take(k)
                    .ToList();
            }
        }

        private static string GetNodeKey(byte[] nodeId)
        {
            return Convert.ToBase64String(nodeId);
        }
    }
}