using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Orchestration.PieceSelection;
using Xunit;

namespace Dotp2pNet.Tests.Unit.PieceSelection;

public class RarestFirstPieceSelectorTests
{
    [Fact]
    public void SelectNextPiece_WithNoPeers_ReturnsNull()
    {
        // Arrange
        var selector = new RarestFirstPieceSelector(42);
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
        var selector = new RarestFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);
        
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
    public void SelectNextPiece_SelectsRarestPiece()
    {
        // Arrange
        var selector = new RarestFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);

        // Peer 1 has pieces 1, 2, 3
        var peer1Bitfield = new TestBitfield(10);
        peer1Bitfield.SetPiece(1);
        peer1Bitfield.SetPiece(2);
        peer1Bitfield.SetPiece(3);

        // Peer 2 has pieces 2, 3, 4
        var peer2Bitfield = new TestBitfield(10);
        peer2Bitfield.SetPiece(2);
        peer2Bitfield.SetPiece(3);
        peer2Bitfield.SetPiece(4);

        // Peer 3 has pieces 3, 4, 5
        var peer3Bitfield = new TestBitfield(10);
        peer3Bitfield.SetPiece(3);
        peer3Bitfield.SetPiece(4);
        peer3Bitfield.SetPiece(5);

        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peer1Bitfield },
            { Guid.NewGuid(), peer2Bitfield },
            { Guid.NewGuid(), peer3Bitfield }
        };

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        // Piece 1 and 5 are rarest (only 1 peer has each)
        // Piece 2 and 4 have 2 peers
        // Piece 3 has 3 peers
        Assert.NotNull(result);
        Assert.True(result.Value == 1 || result.Value == 5);
    }

    [Fact]
    public void SelectNextPiece_WithSingleRarestPiece_ReturnsThatPiece()
    {
        // Arrange
        var selector = new RarestFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);

        // Peer 1 has pieces 1, 2, 3, 4
        var peer1Bitfield = new TestBitfield(10);
        peer1Bitfield.SetPiece(1);
        peer1Bitfield.SetPiece(2);
        peer1Bitfield.SetPiece(3);
        peer1Bitfield.SetPiece(4);

        // Peer 2 has pieces 2, 3, 4
        var peer2Bitfield = new TestBitfield(10);
        peer2Bitfield.SetPiece(2);
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
        // Piece 1 is the rarest (only peer1 has it)
        Assert.Equal(1, result);
    }

    [Fact]
    public void SelectNextPiece_WithEquallyRarePieces_SelectsRandomly()
    {
        // Arrange
        var selector = new RarestFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);

        // All peers have all pieces equally
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
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.NotNull(result);
        Assert.InRange(result.Value, 0, 9);
    }

    [Fact]
    public void SelectNextPiece_IgnoresPiecesWeAlreadyHave()
    {
        // Arrange
        var selector = new RarestFirstPieceSelector(42);
        var localBitfield = new TestBitfield(10);
        localBitfield.SetPiece(1); // We already have piece 1

        // Peer has pieces 1 and 2
        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(1);
        peerBitfield.SetPiece(2);

        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.Equal(2, result); // Should select piece 2, not 1
    }

    [Fact]
    public void SelectNextPiece_WithDeterministicSeed_ProducesSameSequenceForEquallyRare()
    {
        // Arrange
        var selector1 = new RarestFirstPieceSelector(12345);
        var selector2 = new RarestFirstPieceSelector(12345);
        
        var localBitfield = new TestBitfield(10);
        
        // All pieces equally rare
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
