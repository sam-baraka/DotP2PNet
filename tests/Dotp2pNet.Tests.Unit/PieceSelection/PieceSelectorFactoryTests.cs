using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Orchestration.PieceSelection;
using Xunit;

namespace Dotp2pNet.Tests.Unit.PieceSelection;

public class PieceSelectorFactoryTests
{
    [Fact]
    public void GetSelector_WithEmptyBitfield_ReturnsRandomSelector()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        var bitfield = new TestBitfield(0);

        // Act
        var selector = factory.GetSelector(bitfield);

        // Assert
        Assert.IsType<RandomFirstPieceSelector>(selector);
    }

    [Fact]
    public void GetSelector_WithLessThan5Percent_ReturnsRandomSelector()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        var bitfield = new TestBitfield(100);
        
        // Set 4 pieces (4% complete)
        for (int i = 0; i < 4; i++)
        {
            bitfield.SetPiece(i);
        }

        // Act
        var selector = factory.GetSelector(bitfield);

        // Assert
        Assert.IsType<RandomFirstPieceSelector>(selector);
    }

    [Fact]
    public void GetSelector_WithBetween5And95Percent_ReturnsRarestFirstSelector()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        var bitfield = new TestBitfield(100);
        
        // Set 50 pieces (50% complete)
        for (int i = 0; i < 50; i++)
        {
            bitfield.SetPiece(i);
        }

        // Act
        var selector = factory.GetSelector(bitfield);

        // Assert
        Assert.IsType<RarestFirstPieceSelector>(selector);
    }

    [Fact]
    public void GetSelector_WithMoreThan95Percent_ReturnsEndgameSelector()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        var bitfield = new TestBitfield(100);
        
        // Set 96 pieces (96% complete)
        for (int i = 0; i < 96; i++)
        {
            bitfield.SetPiece(i);
        }

        // Act
        var selector = factory.GetSelector(bitfield);

        // Assert
        Assert.IsType<EndgamePieceSelector>(selector);
    }

    [Fact]
    public void GetCurrentStrategyName_ReturnsCorrectNames()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        
        var emptyBitfield = new TestBitfield(100);
        
        var earlyBitfield = new TestBitfield(100);
        for (int i = 0; i < 4; i++) earlyBitfield.SetPiece(i);
        
        var midBitfield = new TestBitfield(100);
        for (int i = 0; i < 50; i++) midBitfield.SetPiece(i);
        
        var lateBitfield = new TestBitfield(100);
        for (int i = 0; i < 96; i++) lateBitfield.SetPiece(i);

        // Act & Assert
        Assert.Equal("Random-First", factory.GetCurrentStrategyName(emptyBitfield));
        Assert.Equal("Random-First", factory.GetCurrentStrategyName(earlyBitfield));
        Assert.Equal("Rarest-First", factory.GetCurrentStrategyName(midBitfield));
        Assert.Equal("Endgame", factory.GetCurrentStrategyName(lateBitfield));
    }

    [Fact]
    public void SelectNextPiece_DelegatesToCorrectSelector()
    {
        // Arrange
        var factory = new PieceSelectorFactory(42);
        var bitfield = new TestBitfield(100);
        
        // Set 50 pieces (50% complete) - should use rarest-first
        for (int i = 0; i < 50; i++)
        {
            bitfield.SetPiece(i);
        }

        var peerBitfield = new TestBitfield(100);
        peerBitfield.SetPiece(51);
        peerBitfield.SetPiece(52);
        
        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Act
        var result = factory.SelectNextPiece(bitfield, peerBitfields);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Value == 51 || result.Value == 52);
    }

    [Fact]
    public void GetEndgameSelector_ReturnsSameInstance()
    {
        // Arrange
        var factory = new PieceSelectorFactory();

        // Act
        var selector1 = factory.GetEndgameSelector();
        var selector2 = factory.GetEndgameSelector();

        // Assert
        Assert.Same(selector1, selector2);
    }

    [Fact]
    public void GetProgressInfo_ReturnsCorrectInformation()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        var bitfield = new TestBitfield(100);
        
        // Set 50 pieces (50% complete)
        for (int i = 0; i < 50; i++)
        {
            bitfield.SetPiece(i);
        }

        // Act
        var (progress, strategy, complete, total) = factory.GetProgressInfo(bitfield);

        // Assert
        Assert.Equal(0.5, progress);
        Assert.Equal("Rarest-First", strategy);
        Assert.Equal(50, complete);
        Assert.Equal(100, total);
    }

    [Fact]
    public void GetProgressInfo_HandlesEmptyBitfield()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        var bitfield = new TestBitfield(0);

        // Act
        var (progress, strategy, complete, total) = factory.GetProgressInfo(bitfield);

        // Assert
        Assert.Equal(0, progress);
        Assert.Equal("Random-First", strategy);
        Assert.Equal(0, complete);
        Assert.Equal(0, total);
    }

    [Fact]
    public void Factory_WithSeed_ProducesDeterministicResults()
    {
        // Arrange
        var factory1 = new PieceSelectorFactory(12345);
        var factory2 = new PieceSelectorFactory(12345);
        
        var bitfield = new TestBitfield(100);
        
        var peerBitfield = new TestBitfield(100);
        for (int i = 0; i < 100; i++)
        {
            peerBitfield.SetPiece(i);
        }
        
        var peerBitfields = new Dictionary<Guid, IBitfield>
        {
            { Guid.NewGuid(), peerBitfield }
        };

        // Act
        var result1 = factory1.SelectNextPiece(bitfield, peerBitfields);
        var result2 = factory2.SelectNextPiece(bitfield, peerBitfields);

        // Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetSelector_TransitionsCorrectlyThroughStages()
    {
        // Arrange
        var factory = new PieceSelectorFactory();
        var bitfield = new TestBitfield(100);

        // Act & Assert - Random stage (0-5%)
        Assert.IsType<RandomFirstPieceSelector>(factory.GetSelector(bitfield));
        
        // Transition to rarest-first (5-95%)
        for (int i = 0; i < 10; i++)
        {
            bitfield.SetPiece(i);
        }
        Assert.IsType<RarestFirstPieceSelector>(factory.GetSelector(bitfield));
        
        // Transition to endgame (95%+)
        for (int i = 10; i < 96; i++)
        {
            bitfield.SetPiece(i);
        }
        Assert.IsType<EndgamePieceSelector>(factory.GetSelector(bitfield));
    }
}
