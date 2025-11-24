# Peer Management

This directory contains components for managing peer selection and the choking algorithm in the P2P network.

## Overview

Peer management is crucial for P2P performance and fairness. The BitTorrent protocol uses a clever "tit-for-tat" strategy combined with optimistic unchoking to achieve both goals.

## Key Concepts

### Tit-for-Tat Strategy

The tit-for-tat strategy rewards peers who upload to us by giving them download slots:

1. **Measure Upload Rates**: Track how much each peer uploads to us
2. **Unchoke Top Performers**: Give download slots to peers with highest upload rates
3. **Re-evaluate Regularly**: Every 10 seconds, recalculate who gets unchoked

This creates a reciprocal relationship: "I'll upload to you if you upload to me."

**Benefits:**
- Encourages cooperation (peers upload to get downloads)
- Discourages free-riding (peers who don't upload get choked)
- Self-organizing and fair

### Choking and Unchoking

**Choking** means refusing to send pieces to a peer, even if they request them.
**Unchoking** means allowing a peer to download pieces from us.

Each peer maintains a limited number of "upload slots" (typically 4):
- 3 slots for regular unchoked peers (based on upload rate)
- 1 slot for optimistic unchoking (random peer)

### Optimistic Unchoking

Optimistic unchoking solves two problems:

1. **Discovery**: How do we find better peers if we only unchoke known good peers?
2. **Bootstrap**: How do new peers get started if everyone uses tit-for-tat?

**Solution**: Randomly unchoke one peer every 30 seconds, regardless of their upload rate.

**Benefits:**
- New peers get a chance to prove themselves
- We discover peers with better connections
- Prevents starvation of new peers

### Peer Selection

When choosing which peers to connect to from a list of available peers:

1. **Reputation Scoring**: Calculate a score based on:
   - Upload/download rates
   - Hash verification success rate
   - Response time
   - Connection stability

2. **Filtering**: Remove:
   - Already connected peers
   - Stale peers (not seen recently)
   - Peers with very low reputation scores
   - Peers with high hash failure rates

3. **Prioritization**: Prefer:
   - Peers with high reputation scores
   - Peers with good upload rates
   - Peers with low hash failure rates

## Implementation Details

### PeerSelector Class

The `PeerSelector` class implements three main operations:

#### 1. SelectPeersToConnect

Chooses which peers to connect to from a list of available peers.

```csharp
var peersToConnect = peerSelector.SelectPeersToConnect(
    availablePeers: allKnownPeers,
    currentConnections: activeConnections,
    maxConnections: 50
);
```

**Algorithm:**
1. Calculate available connection slots
2. Filter out invalid/stale/connected peers
3. Sort by reputation score
4. Take top N peers

#### 2. SelectPeersToUnchoke

Determines which connected peers should be unchoked (allowed to download).

```csharp
var peersToUnchoke = peerSelector.SelectPeersToUnchoke(
    connectedPeers: allConnectedPeers,
    uploadSlots: 4
);
```

**Algorithm:**
1. Check if it's time to re-evaluate (every 10 seconds)
2. Select top N-1 peers by upload rate (tit-for-tat)
3. Select 1 peer for optimistic unchoking
4. Return list of peer IDs to unchoke

#### 3. SelectOptimisticUnchoke

Selects a peer for optimistic unchoking.

```csharp
var optimisticPeer = peerSelector.SelectOptimisticUnchoke(
    connectedPeers: allConnectedPeers,
    currentlyUnchoked: regularUnchokedPeers
);
```

**Algorithm:**
1. Check if it's time to rotate (every 30 seconds)
2. Find peers that are currently choked
3. Prefer new peers (haven't uploaded to yet)
4. Randomly select one peer

## Reputation Scoring

Each peer gets a reputation score (0-100) based on:

| Factor | Impact | Reasoning |
|--------|--------|-----------|
| Hash failures | -10 per 1% failure rate | Corrupted data is very bad |
| Response time | +10 if < 1s, -20 if > 5s | Fast peers are valuable |
| Upload activity | +1 per 10 pieces uploaded | Reward cooperation |

**Example Scores:**
- Perfect peer (no failures, fast, uploads): 100+
- Good peer (occasional failures, decent speed): 80-90
- Mediocre peer (some failures, slow): 50-70
- Bad peer (many failures, very slow): 0-30

## Configuration

Key parameters that can be tuned:

```csharp
// How often to re-evaluate choking (seconds)
private const int ChokingEvaluationSeconds = 10;

// How often to rotate optimistic unchoke (seconds)
private const int OptimisticRotationSeconds = 30;

// Minimum reputation score to connect to a peer
private const double MinimumReputationScore = 20.0;

// Number of upload slots (typically 4)
var uploadSlots = 4;
```

## Example Usage

```csharp
// Create peer selector
var peerSelector = new PeerSelector(logger);

// Select peers to connect to
var availablePeers = await tracker.GetPeersAsync(infoHash);
var peersToConnect = peerSelector.SelectPeersToConnect(
    availablePeers,
    connectionManager.ActiveConnections.Select(c => c.PeerInfo).ToList(),
    maxConnections: 50
);

// Connect to selected peers
foreach (var peer in peersToConnect)
{
    await connectionManager.ConnectToPeerAsync(peer.IpAddress.ToString(), peer.Port, ct);
}

// Periodically update choking
while (downloading)
{
    await Task.Delay(TimeSpan.FromSeconds(10));
    
    var connectedPeers = connectionManager.ActiveConnections
        .Select(c => c.PeerInfo)
        .ToList();
    
    var peersToUnchoke = peerSelector.SelectPeersToUnchoke(
        connectedPeers,
        uploadSlots: 4
    );
    
    // Apply choking decisions
    foreach (var connection in connectionManager.ActiveConnections)
    {
        var shouldUnchoke = peersToUnchoke.Any(id => id.SequenceEqual(connection.PeerId));
        if (shouldUnchoke && connection.AmChoking)
        {
            await connection.SendUnchokeAsync();
        }
        else if (!shouldUnchoke && !connection.AmChoking)
        {
            await connection.SendChokeAsync();
        }
    }
}
```

## Learning Experiments

### Experiment 1: Observe Tit-for-Tat

1. Connect to a swarm with multiple peers
2. Enable debug logging to see choking decisions
3. Observe how peers with higher upload rates get unchoked
4. Try limiting your upload rate and see how it affects your download rate

### Experiment 2: Optimistic Unchoking

1. Connect to a swarm as a new peer with no upload history
2. Observe how you eventually get unchoked via optimistic unchoking
3. Once unchoked, start uploading and see if you get regular unchoke slots

### Experiment 3: Reputation Scoring

1. Simulate a peer with high hash failure rate
2. Observe how its reputation score drops
3. See how it gets deprioritized for connections
4. Compare with a well-behaved peer

## Further Reading

- [BitTorrent Protocol Specification](http://www.bittorrent.org/beps/bep_0003.html)
- ["Incentives Build Robustness in BitTorrent" paper](https://www.bittorrent.org/bittorrentecon.pdf)
- [Understanding BitTorrent's Choking Algorithm](https://wiki.theory.org/BitTorrentSpecification#Choking_and_Optimistic_Unchoking)
