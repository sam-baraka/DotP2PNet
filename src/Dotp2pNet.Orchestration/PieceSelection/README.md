# Piece Selection Strategies

This directory contains implementations of different piece selection strategies for the P2P file transfer system.

## Overview

Piece selection is a critical component of BitTorrent-like protocols. The strategy used to select which piece to download next significantly impacts:
- Download speed
- Swarm health (availability of pieces across peers)
- Trading opportunities with other peers
- Time to first complete piece

## Strategies Implemented

### 1. Random-First (`RandomFirstPieceSelector`)

**When to use:** Initial download phase (0-5% complete)

**How it works:**
- Selects a random piece from all available pieces
- No preference for rare or common pieces

**Advantages:**
- Simple and fast
- Gets something complete quickly
- Good for initial startup

**Disadvantages:**
- Doesn't optimize for swarm health
- May select common pieces

**Use case:** Getting started quickly and avoiding the "last piece problem" where many peers need the same initial piece.

### 2. Rarest-First (`RarestFirstPieceSelector`)

**When to use:** Main download phase (5-95% complete)

**How it works:**
1. Count how many peers have each piece
2. Select the piece that the fewest peers have
3. If multiple pieces are equally rare, pick randomly

**Advantages:**
- Optimal for swarm health
- Distributes rare pieces across the network
- Maximizes trading opportunities
- Prevents pieces from becoming unavailable

**Disadvantages:**
- Slightly more complex than random
- Not optimal for the first few pieces

**Use case:** Standard BitTorrent strategy for the bulk of the download. This is the most important strategy for maintaining a healthy swarm.

### 3. Endgame (`EndgamePieceSelector`)

**When to use:** Final download phase (95-100% complete)

**How it works:**
1. Request ALL remaining pieces from ALL peers that have them
2. When a piece arrives, cancel pending requests for that piece
3. Accept pieces from whichever peer responds first

**Advantages:**
- Minimizes time to completion
- Prevents slow peers from bottlenecking
- Maximizes download speed in final phase

**Disadvantages:**
- Wastes bandwidth on duplicate requests
- Increases network overhead
- Should only be used when nearly complete

**Use case:** Finishing the download as quickly as possible when only a few pieces remain.

## Strategy Selection

The `PieceSelectorFactory` automatically chooses the appropriate strategy based on download progress:

```
0%                    5%                                95%                  100%
├─────────────────────┼─────────────────────────────────┼─────────────────────┤
│   Random-First      │        Rarest-First             │      Endgame        │
└─────────────────────┴─────────────────────────────────┴─────────────────────┘
```

## Usage Example

```csharp
// Create the factory
var factory = new PieceSelectorFactory();

// Get the appropriate selector based on progress
var selector = factory.GetSelector(localBitfield);

// Select the next piece
int? pieceIndex = selector.SelectNextPiece(localBitfield, peerBitfields);

if (pieceIndex.HasValue)
{
    // Request the piece from a peer
    await RequestPieceAsync(pieceIndex.Value);
}
```

## Advanced Usage

### Endgame Mode Tracking

The endgame selector tracks which pieces have been requested to avoid excessive duplication:

```csharp
var endgameSelector = factory.GetEndgameSelector();

// Mark a piece as requested
endgameSelector.MarkPieceRequested(pieceIndex);

// When piece is received
endgameSelector.MarkPieceReceived(pieceIndex);
```

### Progress Information

Get detailed progress information:

```csharp
var (progress, strategy, complete, total) = factory.GetProgressInfo(localBitfield);
Console.WriteLine($"Progress: {progress:P2} using {strategy} strategy");
Console.WriteLine($"Pieces: {complete}/{total}");
```

## Design Decisions

### Why Three Strategies?

1. **Random-First**: Solves the "cold start" problem where many peers need the same first piece
2. **Rarest-First**: Proven optimal strategy for swarm health (BitTorrent standard)
3. **Endgame**: Solves the "last piece" problem where slow peers delay completion

### Threshold Values

- **Random → Rarest transition (5%)**: Early enough to benefit from rarest-first, late enough to have some pieces to trade
- **Rarest → Endgame transition (95%)**: Late enough that bandwidth waste is acceptable, early enough to speed up completion

These thresholds can be adjusted based on:
- Network conditions
- Number of peers
- Piece size
- Total file size

## Performance Considerations

### Time Complexity

- **Random-First**: O(n) where n = number of pieces
- **Rarest-First**: O(n × p) where n = pieces, p = peers
- **Endgame**: O(n × p) where n = pieces, p = peers

### Memory Usage

All strategies use minimal memory:
- Random: O(n) for available pieces list
- Rarest: O(n) for piece counts
- Endgame: O(n) for requested pieces tracking

### Optimization Opportunities

1. **Caching**: Cache piece counts in rarest-first to avoid recalculating
2. **Incremental Updates**: Update counts incrementally when peer bitfields change
3. **Parallel Processing**: Count pieces in parallel for large torrents

## Testing

Each strategy includes:
- Unit tests for selection logic
- Edge case tests (no peers, no pieces, all pieces)
- Integration tests with real bitfields

## Further Reading

- [BitTorrent Protocol Specification (BEP 3)](http://www.bittorrent.org/beps/bep_0003.html)
- [BitTorrent Economics Paper](https://www.bittorrent.org/bittorrentecon.pdf)
- "Incentives Build Robustness in BitTorrent" by Bram Cohen

## Future Enhancements

Potential improvements:
- **Sequential mode**: For streaming video
- **Priority-based selection**: User-specified piece priorities
- **Adaptive thresholds**: Adjust based on swarm conditions
- **Piece availability prediction**: ML-based prediction of piece availability
