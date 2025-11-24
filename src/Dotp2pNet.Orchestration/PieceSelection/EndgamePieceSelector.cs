using Dotp2pNet.Core.Interfaces;

namespace Dotp2pNet.Orchestration.PieceSelection;

/// <summary>
/// Selects pieces for endgame mode when download is nearly complete.
/// </summary>
/// <remarks>
/// Endgame mode is activated when only a few pieces remain.
/// 
/// How it works:
/// - Request all remaining pieces from all peers that have them
/// - When a piece arrives, cancel pending requests for that piece from other peers
/// - This maximizes download speed in the final phase
/// 
/// Why endgame mode?
/// - The last few pieces can take a long time if we wait for specific peers
/// - By requesting from multiple peers, we get pieces as fast as possible
/// - The overhead of duplicate requests is acceptable when so few pieces remain
/// 
/// When to use:
/// - Typically when 95%+ of pieces are complete
/// - Or when fewer than 10-20 pieces remain
/// 
/// Trade-offs:
/// + Minimizes time to completion
/// + Prevents slow peers from bottlenecking the final pieces
/// - Wastes bandwidth on duplicate requests
/// - Increases network overhead
/// - Should only be used when nearly complete
/// </remarks>
public class EndgamePieceSelector : IPieceSelector
{
    private readonly Random _random;
    private readonly HashSet<int> _requestedPieces;

    /// <summary>
    /// Initializes a new instance of the EndgamePieceSelector class.
    /// </summary>
    public EndgamePieceSelector()
    {
        _random = new Random();
        _requestedPieces = new HashSet<int>();
    }

    /// <summary>
    /// Initializes a new instance of the EndgamePieceSelector class with a seed.
    /// </summary>
    /// <param name="seed">Seed for the random number generator (useful for testing).</param>
    public EndgamePieceSelector(int seed)
    {
        _random = new Random(seed);
        _requestedPieces = new HashSet<int>();
    }

    /// <summary>
    /// Marks a piece as requested to track duplicate requests.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece that was requested.</param>
    public void MarkPieceRequested(int pieceIndex)
    {
        _requestedPieces.Add(pieceIndex);
    }

    /// <summary>
    /// Marks a piece as received to stop requesting it.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece that was received.</param>
    public void MarkPieceReceived(int pieceIndex)
    {
        _requestedPieces.Remove(pieceIndex);
    }

    /// <summary>
    /// Clears all tracked requested pieces.
    /// </summary>
    public void ClearRequestedPieces()
    {
        _requestedPieces.Clear();
    }

    public int? SelectNextPiece(IBitfield localBitfield, Dictionary<Guid, IBitfield> peerBitfields)
    {
        if (peerBitfields.Count == 0)
        {
            return null;
        }

        // In endgame mode, we request ALL missing pieces from ALL peers
        // This selector returns pieces even if they've been requested before
        var missingPieces = new List<int>();

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
                missingPieces.Add(i);
            }
        }

        if (missingPieces.Count == 0)
        {
            return null;
        }

        // Prioritize pieces that haven't been requested yet
        var unrequestedPieces = missingPieces
            .Where(p => !_requestedPieces.Contains(p))
            .ToList();

        if (unrequestedPieces.Count > 0)
        {
            // Return a random unrequested piece
            int randomIndex = _random.Next(unrequestedPieces.Count);
            return unrequestedPieces[randomIndex];
        }

        // All pieces have been requested, so return any missing piece
        // This allows requesting the same piece from multiple peers
        int index = _random.Next(missingPieces.Count);
        return missingPieces[index];
    }

    /// <summary>
    /// Determines if endgame mode should be activated based on download progress.
    /// </summary>
    /// <param name="localBitfield">The bitfield representing pieces we have.</param>
    /// <param name="threshold">The percentage threshold for activating endgame (default 95%).</param>
    /// <returns>True if endgame mode should be activated; otherwise, false.</returns>
    public static bool ShouldActivateEndgame(IBitfield localBitfield, double threshold = 0.95)
    {
        if (localBitfield.Length == 0)
        {
            return false;
        }

        double progress = (double)localBitfield.CountSetBits() / localBitfield.Length;
        return progress >= threshold;
    }
}
