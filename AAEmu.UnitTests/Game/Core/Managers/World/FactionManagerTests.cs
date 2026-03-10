using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class FactionManagerTests
{
    /// <summary>
    /// Verifies that FactionManager can be constructed with an injected ILocalizationManager.
    /// The mock is called during Load() which requires a SQLite DB, so mock verification
    /// is covered by integration tests.
    /// </summary>
    [Test]
    public async Task Constructor_WithMockedLocalizationManager_DoesNotThrow()
    {
        var mockLocalization = new Mock<ILocalizationManager>();

        var manager = new FactionManager(mockLocalization.Object);

        await Assert.That(manager).IsNotNull();
        mockLocalization.VerifyNoOtherCalls();
    }
}