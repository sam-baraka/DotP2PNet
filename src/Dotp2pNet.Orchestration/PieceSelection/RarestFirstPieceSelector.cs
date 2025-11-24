using Dotp2pNet.Core.Interfaces;

namespace Dotp2pNet.Orchestration.PieceSelection;

/// <summary>
/// Selects the rarest piece available from connected peers.
/// </summary>
/// <remarks>
/// This is the standard BitTorrent piece selection strategy.
/// 
/// How it works:
/// - Count how many peers have each piece
/// - Select the piece that the fewest peers have
/// - If multiple pieces are equally rare, pick randomly among them
/// 
/// Why rarest-first?
/// - Improves swarm health by distributing rare pieces
/// - Prevents pieces from becoming unavailable if seeders leave
/// - Maximizes the value of each piece we download (we can trade it with more peers)
/// 
/// Trade-offs:
/// + Optimal for swarm health and long-term availability
/// + Increases trading opportunities with other peers
/// - Slightly more complex than random selection
/// - May not be optimal for the first few pieces (use random-first initially)
/// </remarks>
public class RarestFirstPieceSelector : IPieceSelector
{
    private readonly Random _random;

    /// <summary>
    /// Initializes a new instance of the RarestFirstPieceSelector class.
    /// </summary>
    public RarestFirstPieceSelector()
    {
        _random = new Random();
    }

    /// <summary>
    /// Initializes a new instance of the RarestFirstPieceSelector class with a seed.
    /// </summary>
    /// <param name="seed">Seed for the random number generator (useful for testing).</param>
    public RarestFirstPieceSelector(int seed)
    {
        _random = new Random(seed);
    }

    public int? SelectNextPiece(IBitfield localBitfield, Dictionary<Guid, IBitfield> peerBitfields)
    {
        if (peerBitfields.Count == 0)
        {
            return null;
        }

        // Count how many peers have each piece
        var pieceCounts = new Dictionary<int, int>();

        for (int i = 0; i < localBitfield.Length; i++)
        {
            // Skip pieces we already have
            if (localBitfield.HasPiece(i))
            {
                continue;
            }

            // Count how many peers have this piece
            int count = 0;
            foreach (var peerBitfield in peerBitfields.Values)
            {
                if (i < peerBitfield.Length && peerBitfield.HasPiece(i))
                {
                    count++;
                }
            }

            // Only consider pieces that at least one peer has
            if (count > 0)
            {
                pieceCounts[i] = count;
            }
        }

        if (pieceCounts.Count == 0)
        {
            return null;
        }

        // Find the minimum count (rarest pieces)
        int minCount = pieceCounts.Values.Min();

        // Get all pieces with the minimum count
        var rarestPieces = pieceCounts
            .Where(kvp => kvp.Value == minCount)
            .Select(kvp => kvp.Key)
            .ToList();

        // If multiple pieces are equally rare, pick randomly
        if (rarestPieces.Count == 1)
        {
            return rarestPieces[0];
        }

        int randomIndex = _random.Next(rarestPieces.Count);
        return rarestPieces[randomIndex];
    }
}
