using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SusManagerTests
{
    [Test]
    public async Task Constructor_WithMockedWorldManager_DoesNotThrow()
    {
        var mockWorldManager = new Mock<IWorldManager>();

        var manager = new SusManager(mockWorldManager.Object);

        await Assert.That(manager).IsNotNull();
        mockWorldManager.VerifyNoOtherCalls();
    }

    [Test]
    public void ResetAnalyzePlayerDeltaMovement_DoesNotCallWorldManager()
    {
        var mockWorldManager = new Mock<IWorldManager>();
        var manager = new SusManager(mockWorldManager.Object);

        // Should not throw and should not call worldManager
        manager.ResetAnalyzePlayerDeltaMovement(playerId: 42u);

        mockWorldManager.VerifyNoOtherCalls();
    }
}