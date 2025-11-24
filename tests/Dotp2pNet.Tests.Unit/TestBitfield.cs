using Dotp2pNet.Core.Interfaces;

namespace Dotp2pNet.Tests.Unit;

/// <summary>
/// Simple bitfield implementation for testing purposes.
/// </summary>
public class TestBitfield : IBitfield
{
    private readonly bool[] _bits;

    public TestBitfield(int length)
    {
        _bits = new bool[length];
    }

    public int Length => _bits.Length;

    public bool HasPiece(int pieceIndex)
    {
        if (pieceIndex < 0 || pieceIndex >= _bits.Length)
        {
            return false;
        }
        return _bits[pieceIndex];
    }

    public void SetPiece(int pieceIndex)
    {
        if (pieceIndex >= 0 && pieceIndex < _bits.Length)
        {
            _bits[pieceIndex] = true;
        }
    }

    public void ClearPiece(int pieceIndex)
    {
        if (pieceIndex >= 0 && pieceIndex < _bits.Length)
        {
            _bits[pieceIndex] = false;
        }
    }

    public byte[] ToBytes()
    {
        int byteCount = (_bits.Length + 7) / 8;
        byte[] bytes = new byte[byteCount];

        for (int i = 0; i < _bits.Length; i++)
        {
            if (_bits[i])
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                bytes[byteIndex] |= (byte)(1 << (7 - bitIndex));
            }
        }

        return bytes;
    }

    public int CountSetBits()
    {
        return _bits.Count(b => b);
    }
}
