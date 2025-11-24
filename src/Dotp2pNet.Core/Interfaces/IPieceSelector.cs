namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Defines a strategy for selecting the next piece to download.
/// </summary>
/// <remarks>
/// Different strategies optimize for different goals:
/// - Random: Get something complete quickly (good for initial pieces)
/// - Rarest-first: Improve swarm health by prioritizing rare pieces
/// - Endgame: Maximize speed when almost complete by requesting from multiple peers
/// </remarks>
public interface IPieceSelector
{
    /// <summary>
    /// Selects the next piece to download based on the strategy.
    /// </summary>
    /// <param name="localBitfield">The bitfield representing pieces we already have.</param>
    /// <param name="peerBitfields">Dictionary mapping peer connection IDs to their bitfields.</param>
    /// <returns>
    /// The index of the piece to download next, or null if no suitable piece is available.
    /// </returns>
    /// <remarks>
    /// The selector should:
    /// - Only select pieces we don't have (not set in localBitfield)
    /// - Only select pieces that at least one peer has
    /// - Apply strategy-specific logic (random, rarest-first, etc.)
    /// </remarks>
    int? SelectNextPiece(IBitfield localBitfield, Dictionary<Guid, IBitfield> peerBitfields);
}
