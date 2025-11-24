using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Orchestration.PieceSelection;
using Xunit;

namespace Dotp2pNet.Tests.Unit.PieceSelection;

public class RandomFirstPieceSelectorTests
{
    [Fact]
    public void SelectNextPiece_WithNoPeers_ReturnsNull()
    {
        // Arrange
        var selector = new RandomFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);
        var peerBitfields = new Dictionary<Guid, IBitfield>();

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SelectNextPiece_WithAllPiecesOwned_ReturnsNull()
    {
        // Arrange
        var selector = new RandomFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);
        
        // Mark all pieces as owned
        for (int i = 0; i < 10; i++)
        {
            localBitfield.SetPiece(i);
        }

        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(0);
        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SelectNextPiece_WithNoPeerHavingMissingPieces_ReturnsNull()
    {
        // Arrange
        var selector = new RandomFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);
        localBitfield.SetPiece(0);
        localBitfield.SetPiece(1);

        // Peer has same pieces as us
        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(0);
        peerBitfield.SetPiece(1);
        
        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SelectNextPiece_WithAvailablePieces_ReturnsValidPiece()
    {
        // Arrange
        var selector = new RandomFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);
        localBitfield.SetPiece(0);

        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(1);
        peerBitfield.SetPiece(2);
        peerBitfield.SetPiece(3);
        
        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.NotNull(result);
        Assert.InRange(result.Value, 1, 3);
        Assert.False(localBitfield.HasPiece(result.Value));
        Assert.True(peerBitfield.HasPiece(result.Value));
    }

    [Fact]
    public void SelectNextPiece_WithMultiplePeers_SelectsFromAvailablePieces()
    {
        // Arrange
        var selector = new RandomFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);
        localBitfield.SetPiece(0);

        var peer1Bitfield = new TestBitfield(10);
        peer1Bitfield.SetPiece(1);
        peer1Bitfield.SetPiece(2);

        var peer2Bitfield = new TestBitfield(10);
        peer2Bitfield.SetPiece(3);
        peer2Bitfield.SetPiece(4);
        
        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peer1Bitfield },
            { Guid.NewGuid(), peer2Bitfield }
        };

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.NotNull(result);
        Assert.InRange(result.Value, 1, 4);
        Assert.False(localBitfield.HasPiece(result.Value));
    }

    [Fact]
    public void SelectNextPiece_WithDeterministicSeed_ProducesSameSequence()
    {
        // Arrange
        var selector1 = new RandomFirstPieceSelector(12345);
        var selector2 = new RandomFirstPieceSelector(12345);
        
        var localBitfield = new TestBitfield(10);
        var peerBitfield = new TestBitfield(10);
        for (int i = 0; i < 10; i++)
        {
            peerBitfield.SetPiece(i);
        }
        
        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Act
        var result1 = selector1.SelectNextPiece(localBitfield, peerBitfields);
        var result2 = selector2.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.Equal(result1, result2);
    }
}
