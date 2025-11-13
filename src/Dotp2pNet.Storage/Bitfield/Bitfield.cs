using System.Collections;
using Dotp2pNet.Core.Interfaces;

namespace Dotp2pNet.Storage.Bitfield;

/// <summary>
/// Represents a compact bitfield for tracking piece availability.
/// </summary>
/// <remarks>
/// Uses a BitArray internally for efficient bit-level storage.
/// Each bit represents whether a specific piece is available (1) or not (0).
/// Thread-safe for concurrent access.
/// </remarks>
public class Bitfield : IBitfield
{
    private readonly BitArray _bits;
    private readonly object _lock = new();

    public int Length { get; }

    /// <summary>
    /// Initializes a new instance of the Bitfield class.
    /// </summary>
    /// <param name="length">Total number of pieces to track.</param>
    public Bitfield(int length)
    {
        if (length <= 0)
        {
            throw new ArgumentException("Length must be positive", nameof(length));
        }

        Length = length;
        _bits = new BitArray(length, false);
    }

    /// <summary>
    /// Initializes a new instance of the Bitfield class from a byte array.
    /// </summary>
    /// <param name="bytes">Byte array representation of the bitfield.</param>
    /// <param name="length">Total number of pieces (bits may not align to byte boundaries).</param>
    public Bitfield(byte[] bytes, int length)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (length <= 0)
        {
            throw new ArgumentException("Length must be positive", nameof(length));
        }

        Length = length;
        _bits = new BitArray(bytes);

        // Ensure we only consider the first 'length' bits
        if (_bits.Length > length)
        {
            for (int i = length; i < _bits.Length; i++)
            {
                _bits[i] = false;
            }
        }
    }

    public bool HasPiece(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= Length)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        lock (_lock)
        {
            return _bits[pieceIndex];
        }
    }

    public void SetPiece(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= Length)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        lock (_lock)
        {
            _bits[pieceIndex] = true;
        }
    }

    public void ClearPiece(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= Length)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        lock (_lock)
        {
            _bits[pieceIndex] = false;
        }
    }

    public byte[] ToBytes()
    {
        lock (_lock)
        {
            // Calculate number of bytes needed
            var byteCount = (Length + 7) / 8;
            var bytes = new byte[byteCount];

            _bits.CopyTo(bytes, 0);

            return bytes;
        }
    }

    public int CountSetBits()
    {
        lock (_lock)
        {
            int count = 0;
            for (int i = 0; i < Length; i++)
            {
                if (_bits[i])
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Gets all piece indices that are marked as available.
    /// </summary>
    /// <returns>Enumerable of available piece indices.</returns>
    public IEnumerable<int> GetAvailablePieces()
    {
        lock (_lock)
        {
            for (int i = 0; i < Length; i++)
            {
                if (_bits[i])
                {
                    yield return i;
                }
            }
        }
    }

    /// <summary>
    /// Gets all piece indices that are marked as unavailable.
    /// </summary>
    /// <returns>Enumerable of missing piece indices.</returns>
    public IEnumerable<int> GetMissingPieces()
    {
        lock (_lock)
        {
            for (int i = 0; i < Length; i++)
            {
                if (!_bits[i])
                {
                    yield return i;
                }
            }
        }
    }

    /// <summary>
    /// Creates a copy of this bitfield.
    /// </summary>
    /// <returns>A new Bitfield instance with the same state.</returns>
    public Bitfield Clone()
    {
        lock (_lock)
        {
            var bytes = ToBytes();
            return new Bitfield(bytes, Length);
        }
    }

    public override string ToString()
    {
        lock (_lock)
        {
            var setBits = CountSetBits();
            var percentage = (setBits * 100.0) / Length;
            return $"Bitfield: {setBits}/{Length} pieces ({percentage:F1}%)";
        }
    }
}
