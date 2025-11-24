# Piece Selection Strategies

This directory contains implementations of different piece selection strategies for optimizing P2P file downloads.

## Overview

Piece selection is a critical component of BitTorrent-like protocols. The strategy used to select which piece to download next significantly impacts:
- Download speed
- Swarm health (availability of rare pieces)
- Time to first complete piece
- Overall completion time

## Strategies

### 1. RandomFirstPieceSelector

**When to use:** Early download phase (0-5% complete)

**How it works:**
- Randomly selects from available pieces that peers have
- No preference for rare or common pieces

**Advantages:**
- Simple and fast
- Gets something complete quickly
- Avoids the "last piece problem" early on
- Good for testing and debugging

**Trade-offs:**
- Doesn't optimize for swarm health
- May select common pieces that many peers already have

**Example:**
```csharp
var selector = new RandomFirstPieceSelector();
var nextPiece = selector.SelectNextPiece(localBitfield, peerBitfields);
```

### 2. RarestFirstPieceSelector

**When to use:** Middle download phase (5-95% complete)

**How it works:**
1. Counts how many peers have each piece
2. Selects the piece that the fewest peers have
3. If multiple pieces are equally rare, picks randomly among them

**Advantages:**
- Optimal for swarm health and long-term availability
- Prevents pieces from becoming unavailable if seeders leave
- Maximizes the value of each piece (can trade with more peers)
- Increases trading opportunities with other peers

**Trade-offs:**
- Slightly more complex than random selection
- May not be optimal for the first few pieces

**Example:**
```csharp
var selector = new RarestFirstPieceSelector();
var nextPiece = selector.SelectNextPiece(localBitfield, peerBitfields);
```

**Why rarest-first?**

This is the standard BitTorrent strategy because:
- It distributes rare pieces throughout the swarm
- It prevents pieces from becoming extinct if seeders leave
- It maximizes the utility of each downloaded piece (you can trade it with more peers)
- It improves overall swarm health

### 3. EndgamePieceSelector

**When to use:** Final download phase (95%+ complete)

**How it works:**
1. Requests ALL remaining pieces from ALL peers that have them
2. When a piece arrives, cancel pending requests for that piece from other peers
3. Tracks which pieces have been requested to prioritize unrequested ones

**Advantages:**
- Minimizes time to completion
- Prevents slow peers from bottlenecking the final pieces
- Acceptable overhead when so few pieces remain

**Trade-offs:**
- Wastes bandwidth on duplicate requests
- Increases network overhead
- Should only be used when nearly complete

**Example:**
```csharp
var selector = new EndgamePieceSelector();

// Check if we should activate endgame mode
if (EndgamePieceSelector.ShouldActivateEndgame(localBitfield, threshold: 0.95))
{
    var nextPiece = selector.SelectNextPiece(localBitfield, peerBitfields);
    
    if (nextPiece.HasValue)
    {
        selector.MarkPieceRequested(nextPiece.Value);
        // Request the piece...
    }
}

// When a piece is received
selector.MarkPieceReceived(pieceIndex);
```

**Why endgame mode?**

The last few pieces can take a long time if we wait for specific peers:
- Some peers may be slow or unreliable
- Waiting for a single peer to deliver the last piece is inefficient
- By requesting from multiple peers, we get pieces as fast as possible
- The bandwidth overhead is acceptable when so few pieces remain

## PieceSelectorFactory

The factory automatically selects the appropriate strategy based on download progress.

**Strategy Transitions:**
```
0% ──────► 5% ──────► 95% ──────► 100%
   Random      Rarest-First    Endgame
```

**Example:**
```csharp
var factory = new PieceSelectorFactory();

// Automatically selects the right strategy
var nextPiece = factory.SelectNextPiece(localBitfield, peerBitfields);

// Get current strategy info
var (progress, strategy, complete, total) = factory.GetProgressInfo(localBitfield);
Console.WriteLine($"Progress: {progress:P2} using {strategy} strategy");

// Get the current strategy name
var strategyName = factory.GetCurrentStrategyName(localBitfield);
```

## Design Decisions

### Interface-Based Design

All selectors implement `IPieceSelector`:
```csharp
public interface IPieceSelector
{
    int? SelectNextPiece(IBitfield localBitfield, Dictionary<Guid, IBitfield> peerBitfields);
}
```

This enables:
- Polymorphism and strategy pattern
- Easy testing with different strategies
- Runtime strategy switching

### Deterministic Testing

All selectors support seeded random number generators:
```csharp
var selector = new RandomFirstPieceSelector(seed: 12345);
```

This enables:
- Reproducible test results
- Debugging of specific scenarios
- Verification of algorithm correctness

### Thread Safety

The selectors themselves are stateless (except EndgamePieceSelector's tracking):
- Safe to call from multiple threads
- No internal locking required
- Caller is responsible for synchronizing bitfield access

## Performance Considerations

### RandomFirstPieceSelector
- **Time Complexity:** O(n) where n is the number of pieces
- **Space Complexity:** O(n) for the available pieces list
- **Optimization:** Could use reservoir sampling for very large torrents

### RarestFirstPieceSelector
- **Time Complexity:** O(n × p) where n is pieces and p is peers
- **Space Complexity:** O(n) for the piece counts dictionary
- **Optimization:** Could maintain sorted data structure for large swarms

### EndgamePieceSelector
- **Time Complexity:** O(n) where n is the number of pieces
- **Space Complexity:** O(r) where r is requested pieces (typically small)
- **Optimization:** HashSet provides O(1) lookup for requested pieces

## Testing

Comprehensive unit tests cover:
- Edge cases (no peers, all pieces owned, empty bitfields)
- Correct piece selection logic for each strategy
- Proper strategy transitions in factory
- Deterministic behavior with seeded random

Run tests:
```bash
dotnet test tests/Dotp2pNet.Tests.Unit/Dotp2pNet.Tests.Unit.csproj
```

## Further Reading

- [BitTorrent Protocol Specification (BEP 3)](http://www.bittorrent.org/beps/bep_0003.html)
- [Incentives Build Robustness in BitTorrent](https://www.bittorrent.org/bittorrentecon.pdf)
- [Analysis of BitTorrent's Two Kademlia-Based DHTs](https://www.cs.helsinki.fi/u/lxwang/publications/P2P2012_13.pdf)

## Related Components

- `IBitfield` - Tracks which pieces are available
- `IPeerConnection` - Manages connections to peers
- `PieceManager` - Handles piece storage and verification
- `TorrentEngine` - Orchestrates the overall download process
