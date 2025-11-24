using Dotp2pNet.Core.Interfaces;

namespace Dotp2pNet.Orchestration.PieceSelection;

/// <summary>
/// Factory for creating and managing piece selection strategies.
/// </summary>
/// <remarks>
/// The factory chooses the appropriate strategy based on download state:
/// 
/// 1. Random-First (0-5% complete):
///    - Get something complete quickly
///    - Avoid the "last piece problem" early on
/// 
/// 2. Rarest-First (5-95% complete):
///    - Standard BitTorrent strategy
///    - Optimizes for swarm health
///    - Maximizes trading opportunities
/// 
/// 3. Endgame (95-100% complete):
/// ///    - Request remaining pieces from all peers
///    time to completion
///    - Acceptable bandwidth overhead when so few pieces remain
/// 
/// This progressive strategy balances:
/// - Quick startup (random)
/// - Optimal swarm health (rarest-first)
/// - Fast completion (endgame)
/// </remarks>
public class PieceSelectorFactory
{
    private readonly RandomFirstPieceSelector _randomSelector;
    private readonly RarestFirstPieceSelector _rarestSelector;
    private readonly EndgamePieceSelector _endgameSelector;

    private const double RandomFirstThreshold = 0.05;  // Use random for first 5%
    private const double EndgameThreshold = 0.95;      // Use endgame for last 5%

    /// <summary>
    /// Initializes a new instance of the PieceSelectorFactory class.
    /// </summary>
    public PieceSelectorFactory()
    {
        _randomSelector = new RandomFirstPieceSelector();
        _rarestSelector = new RarestFirstPieceSelector();
        _endgameSelector = new EndgamePieceSelector();
    }

    /// <summary>
    /// Initializes a new instance of the PieceSelectorFactory class with a seed.
    /// </summary>
    /// <param name="seed">Seed for random number generators (useful for testing).</param>
    public PieceSelectorFactory(int seed)
    {
        _randomSelector = new RandomFirstPieceSelector(seed);
        _rarestSelector = new RarestFirstPieceSelector(seed);
        _endgameSelector = new EndgamePieceSelector(seed);
    }

    /// <summary>
    /// Gets the appropriate piece selector based on download progress.
    /// </summary>
    /// <param name="localBitfield">The bitfield representing pieces we have.</param>
    /// <returns>The piece selector to use for the current download state.</returns>
    public IPieceSelector GetSelector(IBitfield localBitfield)
    {
        if (localBitfield.Length == 0)
        {
            return _randomSelector;
        }

        double progress = (double)localBitfield.CountSetBits() / localBitfield.Length;

        // Early stage: use random selection
        if (progress < RandomFirstThreshold)
        {
            return _randomSelector;
        }

        // Late stage: use endgame mode
        if (progress >= EndgameThreshold)
        {
            return _endgameSelector;
        }

        // Middle stage: use rarest-first
        return _rarestSelector;
    }

    /// <summary>
    /// Gets the current strategy name based on download progress.
    /// </summary>
    /// <param name="localBitfield">The bitfield representing pieces we have.</param>
    /// <returns>The name of the current strategy.</returns>
    public string GetCurrentStrategyName(IBitfield localBitfield)
    {
        if (localBitfield.Length == 0)
        {
            return "Random-First";
        }

        double progress = (double)localBitfield.CountSetBits() / localBitfield.Length;

        if (progress < RandomFirstThreshold)
        {
            return "Random-First";
        }

        if (progress >= EndgameThreshold)
        {
            return "Endgame";
        }

        return "Rarest-First";
    }

    /// <summary>
    /// Selects the next piece using the appropriate strategy.
    /// </summary>
    /// <param name="localBitfield">The bitfield representing pieces we have.</param>
    /// <param name="peerBitfields">Dictionary mapping peer connection IDs to their bitfields.</param>
    /// <returns>The index of the piece to download next, or null if no suitable piece is available.</returns>
    public int? SelectNextPiece(IBitfield localBitfield, Dictionary<Guid, IBitfield> peerBitfields)
    {
        var selector = GetSelector(localBitfield);
        return selector.SelectNextPiece(localBitfield, peerBitfields);
    }

    /// <summary>
    /// Gets the endgame selector for advanced operations like marking pieces.
    /// </summary>
    /// <returns>The endgame piece selector instance.</returns>
    public EndgamePieceSelector GetEndgameSelector()
    {
        return _endgameSelector;
    }

    /// <summary>
    /// Gets download progress statistics.
    /// </summary>
    /// <param name="localBitfield">The bitfield representing pieces we have.</param>
    /// <returns>A tuple containing (progress percentage, current strategy, pieces complete, total pieces).</returns>
    public (double Progress, string Strategy, int Complete, int Total) GetProgressInfo(IBitfield localBitfield)
    {
        int complete = localBitfield.CountSetBits();
        int total = localBitfield.Length;
        double progress = total > 0 ? (double)complete / total : 0;
        string strategy = GetCurrentStrategyName(localBitfield);

        return (progress, strategy, complete, total);
    }
}
