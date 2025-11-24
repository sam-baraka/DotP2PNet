using Dotp2pNet.Core.Interfaces;

namespace Dotp2pNet.Orchestration.PieceSelection;

/// <summary>
/// Selects a random piece from available pieces.
/// </summary>
/// <remarks>
/// This strategy is useful for:
/// - Initial piece selection to get something complete quickly
/// - Avoiding the "last piece problem" where many peers need the same piece
/// - Testing and debugging
/// 
/// Trade-offs:
/// + Simple and fast
/// + Good for getting started quickly
/// - Doesn't optimize for swarm health
/// - May select common pieces that many peers already have
/// </remarks>
public class RandomFirstPieceSelector : IPieceSelector
{
    private readonly Random _random;

    /// <summary>
    /// Initializes a new instance of the RandomFirstPieceSelector class.
    /// </summary>
    public RandomFirstPieceSelector()
    {
        _random = new Random();
    }

    /// <summary>
    /// Initializes a new instance of the RandomFirstPieceSelector class with a seed.
    /// </summary>
    /// <param name="seed">Seed for the random number generator (useful for testing).</param>
    public RandomFirstPieceSelector(int seed)
    {
        _random = new Random(seed);
    }

    public int? SelectNextPiece(IBitfield localBitfield, Dictionary<Guid, IBitfield> peerBitfields)
    {
        if (peerBitfields.Count == 0)
        {
            return null;
        }

        // Find all pieces we don't have that at least one peer has
        var availablePieces = new List<int>();

        for (int i = 0; i < localBitfield.Length; i++)
        {
            // Skip pieces we already have
            if (localBitfield.HasPiece(i))
            {
                continue;
            }

            // Check if any peer has this piece
            bool anyPeerHasPiece = peerBitfields.Values.Any(peerBitfield => 
                i < peerBitfield.Length && peerBitfield.HasPiece(i));

            if (anyPeerHasPiece)
            {
                availablePieces.Add(i);
            }
        }

        // Return a random piece from available pieces
        if (availablePieces.Count == 0)
        {
            return null;
        }

        int randomIndex = _random.Next(availablePieces.Count);
        return availablePieces[randomIndex];
    }
}
