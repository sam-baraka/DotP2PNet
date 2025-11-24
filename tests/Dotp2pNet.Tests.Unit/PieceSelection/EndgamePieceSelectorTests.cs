using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Orchestration.PieceSelection;
using Xunit;

namespace Dotp2pNet.Tests.Unit.PieceSelection;

public class EndgamePieceSelectorTests
{
    [Fact]
    public void SelectNextPiece_WithNoPeers_ReturnsNull()
    {
        // Arrange
        var selector = new EndgamePieceSelector(42);
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
        var selector = new EndgamePieceSelector(42);
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
    public void SelectNextPiece_PrioritizesUnrequestedPieces()
    {
        // Arrange
        var selector = new EndgamePieceSelector(42);
        var localBitfield = new TestBitfield(10);

        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(1);
        peerBitfield.SetPiece(2);
        peerBitfield.SetPiece(3);

        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Mark piece 1 as requested
        selector.MarkPieceRequested(1);

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        Assert.NotNull(result);
        // Should select piece 2 or 3 (unrequested), not piece 1
        Assert.True(result.Value == 2 || result.Value == 3);
    }

    [Fact]
    public void SelectNextPiece_AllowsDuplicateRequestsWhenAllPiecesRequested()
    {
        // Arrange
        var selector = new EndgamePieceSelector(42);
        var localBitfield = new TestBitfield(10);

        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(1);
        peerBitfield.SetPiece(2);

        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Mark all available pieces as requested
        selector.MarkPieceRequested(1);
        selector.MarkPieceRequested(2);

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        // Should still return a piece even though all are requested
        Assert.NotNull(result);
        Assert.True(result.Value == 1 || result.Value == 2);
    }

    [Fact]
    public void MarkPieceReceived_RemovesFromRequestedSet()
    {
        // Arrange
        var selector = new EndgamePieceSelector(42);
        var localBitfield = new TestBitfield(10);

        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(1);
        peerBitfield.SetPiece(2);

        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Mark pieces as requested
        selector.MarkPieceRequested(1);
        selector.MarkPieceRequested(2);

        // Mark piece 1 as received
        selector.MarkPieceReceived(1);

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        // Should prefer piece 1 now since it's no longer marked as requested
        Assert.Equal(1, result);
    }

    [Fact]
    public void ClearRequestedPieces_ResetsAllTracking()
    {
        // Arrange
        var selector = new EndgamePieceSelector(42);
        var localBitfield = new TestBitfield(10);

        var peerBitfield = new TestBitfield(10);
        peerBitfield.SetPiece(1);
        peerBitfield.SetPiece(2);

        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Mark all pieces as requested
        selector.MarkPieceRequested(1);
        selector.MarkPieceRequested(2);

        // Clear all
        selector.ClearRequestedPieces();

        // Act
        var result = selector.SelectNextPiece(localBitfield, peerBitfields);

        // Assert
        // Should be able to select any piece now
        Assert.NotNull(result);
        Assert.True(result.Value == 1 || result.Value == 2);
    }

    [Fact]
    public void ShouldActivateEndgame_ReturnsTrueWhenAboveThreshold()
    {
        // Arrange
        var bitfield = new TestBitfield(100);
        
        // Set 96 pieces (96% complete)
        for (int i = 0; i < 96; i++)
        {
            bitfield.SetPiece(i);
        }

        // Act
        var result = EndgamePieceSelector.ShouldActivateEndgame(bitfield, 0.95);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldActivateEndgame_ReturnsFalseWhenBelowThreshold()
    {
        // Arrange
        var bitfield = new TestBitfield(100);
        
        // Set 90 pieces (90% complete)
        for (int i = 0; i < 90; i++)
        {
            bitfield.SetPiece(i);
        }

        // Act
        var result = EndgamePieceSelector.ShouldActivateEndgame(bitfield, 0.95);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldActivateEndgame_ReturnsFalseForEmptyBitfield()
    {
        // Arrange
        var bitfield = new TestBitfield(0);

        // Act
        var result = EndgamePieceSelector.ShouldActivateEndgame(bitfield);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldActivateEndgame_UsesCustomThreshold()
    {
        // Arrange
        var bitfield = new TestBitfield(100);
        
        // Set 85 pieces (85% complete)
        for (int i = 0; i < 85; i++)
        {
            bitfield.SetPiece(i);
        }

        // Act
        var resultWith80 = EndgamePieceSelector.ShouldActivateEndgame(bitfield, 0.80);
        var resultWith90 = EndgamePieceSelector.ShouldActivateEndgame(bitfield, 0.90);

        // Assert
        Assert.True(resultWith80);  // 85% > 80%
        Assert.False(resultWith90); // 85% < 90%
    }
}
