# DHT Iterative Lookup

## Overview

The `DhtLookup` class implements the Kademlia iterative lookup algorithm for finding nodes and peers in a distributed hash table (DHT) network. This is a core component of decentralized peer discovery in P2P systems.

## What is Iterative Lookup?

In a DHT, no single node knows about all other nodes. Instead, each node maintains a routing table of a subset of nodes. To find a specific target (node ID or info hash), we use an **iterative lookup** algorithm that progressively queries nodes that are closer and closer to the target.

### The Algorithm

1. **Initialize**: Start with the K (typically 8) closest nodes from our local routing table
2. **Query**: Send queries to alpha (typically 3) of the closest unqueried nodes in parallel
3. **Update**: Add any nodes returned by the queries to our candidate list
4. **Sort**: Re-sort all candidates by XOR distance to the target
5. **Repeat**: Continue querying until convergence
6. **Converge**: Stop when the K closest nodes have all been queried

### Why Parallel Queries (Alpha)?

The algorithm queries **alpha=3** nodes in parallel for several reasons:

- **Speed**: Parallel queries significantly reduce total lookup time
- **Fault Tolerance**: If one node is slow or unresponsive, others can still make progress
- **Network Efficiency**: Alpha=3 balances speed with not overwhelming the network
- **Convergence**: Multiple parallel paths help find the target faster

### Convergence Criteria

The lookup terminates when:

1. **K closest nodes queried**: All K closest nodes have been queried, meaning we've exhausted the closest nodes
2. **No new closer nodes**: Queries aren't returning nodes closer than what we already have
3. **Maximum iterations**: Safety limit of 10 iterations to prevent infinite loops
4. **No more candidates**: We've run out of nodes to query

## Key Concepts

### XOR Distance Metric

Kademlia uses XOR (exclusive OR) as a distance metric:

```
distance(A, B) = A XOR B
```

This metric has useful properties:
- **Symmetric**: distance(A, B) = distance(B, A)
- **Triangle inequality**: Helps with routing efficiency
- **Unique**: Each node has a unique distance to any target

### K-Bucket Routing Table

Nodes are organized into buckets based on their distance from our node ID. Each bucket can hold up to K nodes (typically 8). This structure ensures:

- We know more about nearby nodes
- We have some knowledge of distant parts of the ID space
- Efficient O(log n) lookups

### Timeout Handling

Each query has a 5-second timeout. If a node doesn't respond:

- It's marked as failed in the lookup state
- The lookup continues with other nodes
- The node may be marked as bad in the routing table after multiple failures

## Usage Example

```csharp
// Create a DHT node
var dhtNode = new DhtNode(logger);
await dhtNode.StartAsync(6881);

// Bootstrap with known nodes
await dhtNode.BootstrapAsync(bootstrapNodes);

// Create lookup instance
var lookup = new DhtLookup(dhtNode, logger);

// Find peers for a file
byte[] infoHash = /* 20-byte SHA-1 hash */;
var peers = await lookup.FindPeersAsync(infoHash);

// Find closest nodes to a target
byte[] targetId = /* 20-byte node ID */;
var nodes = await lookup.FindClosestNodesAsync(targetId);
```

## Implementation Details

### LookupState Class

The `LookupState` inner class maintains:

- **Candidates**: All nodes discovered during the lookup
- **Queried nodes**: Nodes we've already queried (to avoid duplicates)
- **Responded nodes**: Nodes that successfully responded
- **Target ID**: The ID we're searching for

This state is thread-safe using concurrent collections and locks.

### Query Methods

Two types of queries are supported:

1. **get_peers**: Find peers sharing a specific file (info hash)
   - Returns either peers (if the node has them) or closer nodes
   - Also returns a token for subsequent announce_peer calls

2. **find_node**: Find nodes closest to a target ID
   - Returns the K closest nodes the queried node knows about
   - Used for node discovery and routing table population

### Parallel Query Execution

```csharp
var queryTasks = nodesToQuery.Select(node => 
    QueryNodeForPeersAsync(node, infoHash, lookupState, peers, ct));

await Task.WhenAll(queryTasks);
```

This pattern:
- Creates a task for each node query
- Executes all tasks concurrently
- Waits for all to complete before the next iteration
- Handles individual failures gracefully

## Performance Characteristics

### Time Complexity

- **Best case**: O(log n) iterations where n is the total number of nodes
- **Typical case**: 3-5 iterations for a well-populated DHT
- **Worst case**: 10 iterations (hard limit)

### Network Traffic

- **Queries per iteration**: Up to alpha (3) parallel queries
- **Total queries**: Typically 9-15 queries for a complete lookup
- **Bandwidth**: Minimal - DHT messages are small (< 1 KB each)

### Convergence Speed

With alpha=3 parallel queries:
- **Small DHT (100 nodes)**: 2-3 iterations, ~1-2 seconds
- **Medium DHT (10,000 nodes)**: 4-5 iterations, ~2-3 seconds
- **Large DHT (1,000,000 nodes)**: 5-7 iterations, ~3-5 seconds

## Learning Experiments

### Experiment 1: Observe Convergence

Enable debug logging and watch how the lookup converges:

```csharp
// Set log level to Debug
var peers = await lookup.FindPeersAsync(infoHash);
```

You'll see:
- Initial candidates from routing table
- Parallel queries in each iteration
- Nodes getting closer to the target
- Convergence when K closest nodes are queried

### Experiment 2: Compare Sequential vs Parallel

Modify alpha to 1 (sequential) and compare:

```csharp
// Change Alpha constant to 1
const int Alpha = 1;
```

Observe:
- Lookup takes 3x longer
- More vulnerable to slow/unresponsive nodes
- Same final result but slower convergence

### Experiment 3: Visualize Distance Convergence

Log the distance of the closest node in each iteration:

```csharp
var closestDistance = XorDistance.Calculate(targetId, closestNode.NodeId);
_logger.LogDebug("Iteration {Iteration}: Closest distance = {Distance}", 
    iteration, closestDistance);
```

You'll see the distance decrease exponentially with each iteration.

## Common Issues and Solutions

### Issue: Lookup Returns No Peers

**Causes**:
- DHT not bootstrapped (no initial nodes)
- Info hash not announced by any peers
- Network connectivity issues

**Solutions**:
- Ensure `BootstrapAsync` was called successfully
- Check that `GetRoutingTableSize()` returns > 0
- Verify the info hash is correct

### Issue: Lookup Takes Too Long

**Causes**:
- Many unresponsive nodes in routing table
- Network latency
- Timeout too long

**Solutions**:
- Clean bad nodes from routing table periodically
- Reduce query timeout (currently 5 seconds)
- Increase alpha for more parallelism

### Issue: Lookup Doesn't Converge

**Causes**:
- Routing table has stale nodes
- Network partitioning
- Bug in convergence logic

**Solutions**:
- Implement periodic routing table maintenance
- Check network connectivity
- Verify convergence criteria in `HasConverged()`

## Future Enhancements

1. **Adaptive Alpha**: Adjust parallelism based on network conditions
2. **Caching**: Cache recent lookup results to avoid redundant queries
3. **Shortlist Optimization**: Track the "shortlist" of K closest nodes more efficiently
4. **Metrics**: Add detailed metrics (queries sent, response times, convergence rate)
5. **IPv6 Support**: Extend to support IPv6 addresses

## References

- [Kademlia Paper](https://pdos.csail.mit.edu/~petar/papers/maymounkov-kademlia-lncs.pdf)
- [BEP 5: DHT Protocol](http://www.bittorrent.org/beps/bep_0005.html)
- [BitTorrent DHT Specification](https://www.bittorrent.org/beps/bep_0005.html)
