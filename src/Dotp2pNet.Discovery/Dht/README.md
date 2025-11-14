# DHT (Distributed Hash Table) Implementation

This directory contains the implementation of a Kademlia-based DHT for decentralized peer discovery in Dotp2pNet.

## Overview

The DHT allows peers to find each other without relying on centralized trackers. It implements the BitTorrent DHT protocol (BEP 5) based on the Kademlia algorithm.

## Key Concepts

### XOR Distance Metric

In Kademlia, the "distance" between two node IDs is calculated using XOR (exclusive OR). This creates a metric space where:
- Distance is symmetric: d(A, B) = d(B, A)
- Distance follows triangle inequality: d(A, C) ≤ d(A, B) + d(B, C)
- Distance to self is zero: d(A, A) = 0

Example:
```
Node A: 10110101...
Node B: 10101010...
XOR:    00011111... (distance)
```

The XOR distance determines which k-bucket a node belongs to in the routing table.

### K-Buckets

The routing table is organized into 160 k-buckets (one for each bit position in the 160-bit ID space). Each bucket stores up to K nodes (typically 8) that fall within a specific distance range.

- Bucket 0: Nodes with distance 2^0 to 2^1 - 1
- Bucket 1: Nodes with distance 2^1 to 2^2 - 1
- ...
- Bucket 159: Nodes with distance 2^159 to 2^160 - 1

Nodes in each bucket are kept in order of last-seen time, with the most recently seen nodes at the tail.

### Node States

Nodes can be in one of three states:
- **Good**: Responded recently (< 15 minutes) and has few failures (< 3)
- **Questionable**: Haven't responded recently but hasn't failed too many times
- **Bad**: Failed 3 or more times (removed from routing table)

## RPC Methods

The DHT implements four RPC methods:

### 1. ping

Checks if a node is alive.

**Query:**
```
{
  "t": "aa",           // Transaction ID
  "y": "q",            // Query
  "q": "ping",         // Method
  "a": {
    "id": "..."        // Querying node's ID
  }
}
```

**Response:**
```
{
  "t": "aa",           // Same transaction ID
  "y": "r",            // Response
  "r": {
    "id": "..."        // Responding node's ID
  }
}
```

### 2. find_node

Finds the K closest nodes to a target ID.

**Query:**
```
{
  "t": "aa",
  "y": "q",
  "q": "find_node",
  "a": {
    "id": "...",       // Querying node's ID
    "target": "..."    // Target ID to find nodes close to
  }
}
```

**Response:**
```
{
  "t": "aa",
  "y": "r",
  "r": {
    "id": "...",
    "nodes": "..."     // Compact node info (26 bytes per node)
  }
}
```

### 3. get_peers

Finds peers sharing a file (info hash).

**Query:**
```
{
  "t": "aa",
  "y": "q",
  "q": "get_peers",
  "a": {
    "id": "...",
    "info_hash": "..." // Info hash of the file
  }
}
```

**Response (with peers):**
```
{
  "t": "aa",
  "y": "r",
  "r": {
    "id": "...",
    "token": "...",    // Token for subsequent announce_peer
    "values": [...]    // List of peer addresses (6 bytes each)
  }
}
```

**Response (with nodes):**
```
{
  "t": "aa",
  "y": "r",
  "r": {
    "id": "...",
    "token": "...",
    "nodes": "..."     // Closest nodes to the info hash
  }
}
```

### 4. announce_peer

Announces that we're sharing a file.

**Query:**
```
{
  "t": "aa",
  "y": "q",
  "q": "announce_peer",
  "a": {
    "id": "...",
    "info_hash": "...",
    "port": 6881,      // Port we're listening on
    "token": "..."     // Token from get_peers response
  }
}
```

**Response:**
```
{
  "t": "aa",
  "y": "r",
  "r": {
    "id": "..."
  }
}
```

## Iterative Lookups

To find peers or nodes, the DHT performs iterative lookups:

1. Start with the K closest nodes from our routing table
2. Query alpha (typically 3) of the closest unqueried nodes in parallel
3. Add returned nodes to our list of closest nodes
4. Repeat until we've queried the K closest nodes or no closer nodes are found

This converges in O(log n) hops, where n is the total number of nodes in the network.

## Compact Node Format

Nodes are encoded in a compact format for efficient transmission:

```
[20 bytes: Node ID][4 bytes: IP address][2 bytes: Port]
```

Total: 26 bytes per node

## Compact Peer Format

Peers are encoded in a compact format:

```
[4 bytes: IP address][2 bytes: Port]
```

Total: 6 bytes per peer

## Usage Example

```csharp
// Create and start DHT node
var dhtNode = new DhtNode(logger);
await dhtNode.StartAsync(6881);

// Bootstrap with known nodes
var bootstrapNodes = new List<PeerInfo>
{
    new PeerInfo 
    { 
        PeerId = ..., 
        IpAddress = IPAddress.Parse("router.bittorrent.com"),
        Port = 6881 
    }
};
await dhtNode.BootstrapAsync(bootstrapNodes);

// Find peers sharing a file
var infoHash = ...; // 20-byte info hash
var peers = await dhtNode.FindPeersAsync(infoHash);

// Announce that we're sharing a file
await dhtNode.AnnouncePeerAsync(infoHash, 6881);

// Stop the node
await dhtNode.StopAsync();
```

## Components

- **DhtNode**: Main DHT node implementation
- **RoutingTable**: Manages the k-bucket routing table
- **KBucket**: Individual k-bucket storing up to K nodes
- **XorDistance**: Utilities for calculating XOR distance
- **DhtNodeInfo**: Information about a DHT node
- **DhtMessage**: DHT message types (Query, Response, Error)
- **DhtMessageCodec**: Encodes/decodes DHT messages using bencode

## Learning Resources

- [BEP 5: DHT Protocol](http://www.bittorrent.org/beps/bep_0005.html)
- [Kademlia Paper](https://pdos.csail.mit.edu/~petar/papers/maymounkov-kademlia-lncs.pdf)
- [DHT Visualization](https://kelseyc18.github.io/kademlia_vis/basics/1/)

## Common Pitfalls

1. **Not handling timeouts**: DHT queries can timeout; always use cancellation tokens
2. **Not maintaining routing table**: Periodically ping questionable nodes to keep routing table fresh
3. **Not validating tokens**: Always verify tokens in announce_peer to prevent abuse
4. **Not handling NAT**: Nodes behind NAT may not be reachable; consider this in peer selection

## Future Enhancements

- Implement routing table refresh (periodic find_node for random IDs)
- Add support for IPv6
- Implement security extensions (BEP 42)
- Add metrics and monitoring
