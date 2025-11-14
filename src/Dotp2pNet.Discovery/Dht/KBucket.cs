namespace Dotp2pNet.Discovery.Dht;

/// <summary>
/// Represents a k-bucket in the Kademlia routing table.
/// </summary>
/// <remarks>
/// A k-bucket stores up to K nodes (typically 8) that fall within a specific
/// distance range. Nodes are kept in order of last-seen time, with the most
/// recently seen nodes at the tail.
/// </remarks>
public class KBucket
{
    private readonly List<DhtNodeInfo> _nodes = new();
    private readonly object _lock = new();

    /// <summary>
    /// The maximum number of nodes a bucket can hold (K parameter).
    /// </summary>
    public const int K = 8;

    /// <summary>
    /// Gets the bucket index (0-159).
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the current number of nodes in this bucket.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _nodes.Count;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this bucket is full.
    /// </summary>
    public bool IsFull
    {
        get
        {
            lock (_lock)
            {
                return _nodes.Count >= K;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KBucket"/> class.
    /// </summary>
    /// <param name="index">The bucket index (0-159).</param>
    public KBucket(int index)
    {
        Index = index;
    }

    /// <summary>
    /// Attempts to add a node to the bucket.
    /// </summary>
    /// <param name="node">The node to add.</param>
    /// <returns>True if the node was added; false if the bucket is full.</returns>
    /// <remarks>
    /// If the node already exists, it's moved to the tail (most recently seen).
    /// If the bucket is full, the node is not added (unless we implement
    /// replacement logic for bad nodes).
    /// </remarks>
    public bool TryAddNode(DhtNodeInfo node)
    {
        lock (_lock)
        {
            // Check if node already exists
            var existingNode = _nodes.FirstOrDefault(n => n.NodeId.SequenceEqual(node.NodeId));
            if (existingNode != null)
            {
                // Move to tail (most recently seen)
                _nodes.Remove(existingNode);
                existingNode.MarkSeen();
                _nodes.Add(existingNode);
                return true;
            }

            // If bucket is full, don't add (could implement replacement logic here)
            if (_nodes.Count >= K)
            {
                return false;
            }

            // Add new node to tail
            _nodes.Add(node);
            return true;
        }
    }

    /// <summary>
    /// Removes a node from the bucket.
    /// </summary>
    /// <param name="nodeId">The node ID to remove.</param>
    /// <returns>True if the node was removed; false if not found.</returns>
    public bool RemoveNode(byte[] nodeId)
    {
        lock (_lock)
        {
            var node = _nodes.FirstOrDefault(n => n.NodeId.SequenceEqual(nodeId));
            if (node != null)
            {
                _nodes.Remove(node);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Gets a node by its node ID.
    /// </summary>
    /// <param name="nodeId">The node ID to find.</param>
    /// <returns>The node if found; otherwise, null.</returns>
    public DhtNodeInfo? GetNode(byte[] nodeId)
    {
        lock (_lock)
        {
            return _nodes.FirstOrDefault(n => n.NodeId.SequenceEqual(nodeId));
        }
    }

    /// <summary>
    /// Gets all nodes in this bucket.
    /// </summary>
    /// <returns>A copy of the node list.</returns>
    public List<DhtNodeInfo> GetNodes()
    {
        lock (_lock)
        {
            return new List<DhtNodeInfo>(_nodes);
        }
    }

    /// <summary>
    /// Gets all good nodes in this bucket.
    /// </summary>
    /// <returns>List of good nodes.</returns>
    public List<DhtNodeInfo> GetGoodNodes()
    {
        lock (_lock)
        {
            return _nodes.Where(n => n.IsGood).ToList();
        }
    }

    /// <summary>
    /// Gets the least recently seen node in this bucket.
    /// </summary>
    /// <returns>The least recently seen node, or null if bucket is empty.</returns>
    public DhtNodeInfo? GetLeastRecentlySeen()
    {
        lock (_lock)
        {
            return _nodes.FirstOrDefault();
        }
    }

    /// <summary>
    /// Marks a node as seen, moving it to the tail of the bucket.
    /// </summary>
    /// <param name="nodeId">The node ID to mark as seen.</param>
    /// <returns>True if the node was found and updated; otherwise, false.</returns>
    public bool MarkNodeSeen(byte[] nodeId)
    {
        lock (_lock)
        {
            var node = _nodes.FirstOrDefault(n => n.NodeId.SequenceEqual(nodeId));
            if (node != null)
            {
                _nodes.Remove(node);
                node.MarkSeen();
                _nodes.Add(node);
                return true;
            }
            return false;
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
            var node = _nodes.FirstOrDefault(n => n.NodeId.SequenceEqual(nodeId));
            if (node != null)
            {
                node.MarkFailed();
                
                // Remove bad nodes
                if (node.IsBad)
                {
                    _nodes.Remove(node);
                }
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Removes all bad nodes from the bucket.
    /// </summary>
    /// <returns>The number of nodes removed.</returns>
    public int RemoveBadNodes()
    {
        lock (_lock)
        {
            int removed = _nodes.RemoveAll(n => n.IsBad);
            return removed;
        }
    }
}
