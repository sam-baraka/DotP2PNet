namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Represents the routing table for a Kademlia DHT node.
/// </summary>
/// <remarks>
/// The routing table is organized into 160 k-buckets, one for each bit
/// in the 160-bit node ID space. Each bucket stores up to K nodes that
/// fall within a specific distance range from our node ID.
/// </remarks>
public class RoutingTable
{
    private readonly byte[] _ourNodeId;
    private readonly KBucket[] _buckets;
    private readonly object _lock = new();

    /// <summary>
    /// Gets our node ID.
    /// </summary>
    public byte[] OurNodeId => _ourNodeId;

    /// <summary>
    /// Gets the total number of nodes in the routing table.
    /// </summary>
    public int TotalNodes
    {
        get
        {
            lock (_lock)
            {
                return _buckets.Sum(b => b.Count);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingTable"/> class.
    /// </summary>
    /// <param name="ourNodeId">Our 160-bit node ID (20 bytes).</param>
    public RoutingTable(byte[] ourNodeId)
    {
        if (ourNodeId.Length != 20)
            throw new ArgumentException("Node ID must be exactly 20 bytes (160 bits)", nameof(ourNodeId));

        _ourNodeId = ourNodeId;
        _buckets = new KBucket[160];
        
        for (int i = 0; i < 160; i++)
        {
            _buckets[i] = new KBucket(i);
        }
    }

    /// <summary>
    /// Adds a node to the routing table.
    /// </summary>
    /// <param name="node">The node to add.</param>
    /// <returns>True if the node was added; false if the appropriate bucket is full.</returns>
    public bool AddNode(DhtNodeInfo node)
    {
        if (node.NodeId.SequenceEqual(_ourNodeId))
            return false; // Don't add ourselves

        lock (_lock)
        {
            int bucketIndex = XorDistance.GetBucketIndex(_ourNodeId, node.NodeId);
            return _buckets[bucketIndex].TryAddNode(node);
        }
    }

    /// <summary>
    /// Removes a node from the routing table.
    /// </summary>
    /// <param name="nodeId">The node ID to remove.</param>
    /// <returns>True if the node was removed; false if not found.</returns>
    public bool RemoveNode(byte[] nodeId)
    {
        lock (_lock)
        {
            int bucketIndex = XorDistance.GetBucketIndex(_ourNodeId, nodeId);
            return _buckets[bucketIndex].RemoveNode(nodeId);
        }
    }

    /// <summary>
    /// Gets a node by its node ID.
    ///    /// </summary>
  // <param name="nodeId">The node ID to find.</param>
    /// <returns>The node if found; otherwise, null.</returns>
    public DhtNodeInfo? GetNode(byte[] nodeId)
    {
        lock (_lock)
        {
            int bucketIndex = XorDistance.GetBucketIndex(_ourNodeId, nodeId);
            return _buckets[bucketIndex].GetNode(nodeId);
        }
    }

    /// <summary>
    /// Finds the K closest nodes to a target ID.
    /// </summary>
    /// <param name="targetId">The target ID to find nodes close to.</param>
    /// <param name="count">The maximum number of nodes to return (default K=8).</param>
    /// <returns>List of closest nodes, sorted by distance.</returns>
    public List<DhtNodeInfo> FindClosestNodes(byte[] targetId, int count = KBucket.K)
    {
        lock (_lock)
        {
            // Get all nodes from all buckets
            var allNodes = _buckets
                .SelectMany(b => b.GetGoodNodes())
                .ToList();

            // Sort by distance to target and take the closest K
            return allNodes
                .OrderBy(n => XorDistance.Calculate(targetId, n.NodeId))
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Gets all nodes from the routing table.
    /// </summary>
    /// <returns>List of all nodes.</returns>
    public List<DhtNodeInfo> GetAllNodes()
    {
        lock (_lock)
        {
            return _buckets
                .SelectMany(b => b.GetNodes())
                .ToList();
        }
    }

    /// <summary>
    /// Gets all good nodes from the routing table.
    /// </summary>
    /// <returns>List of all good nodes.</returns>
    public List<DhtNodeInfo> GetAllGoodNodes()
    {
        lock (_lock)
        {
            return _buckets
                .SelectMany(b => b.GetGoodNodes())
                .ToList();
        }
    }

    /// <summary>
    /// Marks a node as seen, updating its last-seen timestamp.
    /// </summary>
    /// <param name="nodeId">The node ID to mark as seen.</param>
    /// <returns>True if the node was found and updated; otherwise, false.</returns>
    public bool MarkNodeSeen(byte[] nodeId)
    {
        lock (_lock)
        {
            int bucketIndex = XorDistance.GetBucketIndex(_ourNodeId, nodeId);
            return _buckets[bucketIndex].MarkNodeSeen(nodeId);
        }
    }

    /// <summary>
    /// Marks a node as failed, incrementing its failure count.
    /// </summary>
    /// <param name="nodeId">The node ID to mark as failed.</param>
    /// <returns>True if the node was found and updated; otherwise, false.</returns>
    public bool MarkNodeFailed(byte[] nodeId)
    {
        lock (_lock)
        {
            int bucketIndex = XorDistance.GetBucketIndex(_ourNodeId, nodeId);
            return _buckets[bucketIndex].MarkNodeFailed(nodeId);
        }
    }

    /// <summary>
    /// Removes all bad nodes from the routing table.
    /// </summary>
    /// <returns>The total number of nodes removed.</returns>
    public int RemoveBadNodes()
    {
        lock (_lock)
        {
            return _buckets.Sum(b => b.RemoveBadNodes());
        }
    }

    /// <summary>
    /// Gets statistics about the routing table.
    /// </summary>
    /// <returns>A dictionary with bucket statistics.</returns>
    public Dictionary<string, int> GetStatistics()
    {
        lock (_lock)
        {
            var stats = new Dictionary<string, int>
            {
                ["TotalNodes"] = TotalNodes,
                ["GoodNodes"] = _buckets.Sum(b => b.GetGoodNodes().Count),
                ["NonEmptyBuckets"] = _buckets.Count(b => b.Count > 0),
                ["FullBuckets"] = _buckets.Count(b => b.IsFull)
            };
            return stats;
        }
    }
}
