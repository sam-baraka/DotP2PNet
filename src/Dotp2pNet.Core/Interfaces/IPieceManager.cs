namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Manages piece selection, validation, and storage for torrent downloads.
/// </summary>
/// <remarks>
/// Coordinates which pieces to request from peers, validates downloaded pieces
/// against their SHA1 hashes, and manages piece storage.
/// </remarks>
public interface IPieceManager
{
    /// <summary>
    /// Gets the total number of pieces in the torrent.
    /// </summary>
    int TotalPieces { get; }

    /// <summary>
    /// Gets the number of pieces we have downloaded and verified.
    /// </summary>
    int CompletedPieces { get; }

    /// <summary>
    /// Gets the size of each piece in bytes (except possibly the last piece).
    /// </summary>
    int PieceLength { get; }

    /// <summary>
    /// Checks if we have a specific piece.
    /// </summary>
    /// /// <param name="piecx">The index of the piece to check.</param>
    /// <returns>True if we have the piece, false otherwise.</returns>
    bool HasPiece(int pieceIndex);

    /// <summary>
    /// Selects the next piece to request from a peer based on availability and strategy.
    /// </summary>
    /// <param name="peerBitfield">The bitfield indicating which pieces the peer has.</param>
    /// <returns>The index of the piece to request, or -1 if no piece is needed.</returns>
    int SelectPiece(IBitfield peerBitfield);

    /// <summary>
    /// Stores a downloaded piece and validates its hash.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece.</param>
    /// <param name="data">The piece data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the piece hash is valid and was stored, false otherwise.</returns>
    Task<bool> StorePieceAsync(int pieceIndex, byte[] data, CancellationToken ct);

    /// <summary>
    /// Retrieves a piece from storage.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The piece data.</returns>
    Task<byte[]> GetPieceAsync(int pieceIndex, CancellationToken ct);

    /// <summary>
    /// Gets our bitfield indicating which pieces we have.
    /// </summary>
    /// <returns>Our bitfield.</returns>
    IBitfield GetBitfield();
}
