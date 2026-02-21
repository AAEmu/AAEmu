using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SusManagerTests
{
    [Fact]
    public void Constructor_WithMockedWorldManager_DoesNotThrow()
    {
        var mockWorldManager = new Mock<IWorldManager>();

        var manager = new SusManager(mockWorldManager.Object);

        Assert.NotNull(manager);
        mockWorldManager.VerifyNoOtherCalls();
    }

    [Fact]
    public void ResetAnalyzePlayerDeltaMovement_DoesNotCallWorldManager()
    {
        var mockWorldManager = new Mock<IWorldManager>();
        var manager = new SusManager(mockWorldManager.Object);

        // Should not throw and should not call worldManager
        manager.ResetAnalyzePlayerDeltaMovement(playerId: 42u);

        mockWorldManager.VerifyNoOtherCalls();
    }
}
