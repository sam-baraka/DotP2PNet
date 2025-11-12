namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Represents a compact bitfield indicating which pieces a peer has.
/// </summary>
/// <remarks>
/// Each bit represents one piece. Bit 0 of byte 0 is piece 0, bit 1 is piece 1, etc.
/// This is used to efficiently communicate piece availability between peers.
/// </remarks>
public interface IBitfield
{
    /// <summary>
    /// Gets the total number of pieces represented by this bitfield.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Checks if a specific piece is marked as available.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece to check.</param>
    /// <returns>True if the piece is available, false otherwise.</returns>
    bool HasPiece(int pieceIndex);

    /// <summary>
    /// Marks a piece as available.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece to mark.</param>
    void SetPiece(int pieceIndex);

    /// <summary>
    /// Marks a piece as unavailable.
    /// </summary>
    /// <param name="pieceIndex">The index of the piece to clear.</param>
    void ClearPiece(int pieceIndex);

    /// <summary>
    /// Gets the raw byte array representation of the bitfield.
    /// </summary>
    /// <returns>Byte array where each bit represents a piece.</returns>
    byte[] ToBytes();

    /// <summary>
    /// Gets the number of pieces marked as available.
    /// </summary>
    /// <returns>Count of available pieces.</returns>
    int CountSetBits();
}
