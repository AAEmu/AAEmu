using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class FactionManagerTests
{
    /// <summary>
    /// Verifies that FactionManager can be constructed with an injected ILocalizationManager.
    /// The mock is called during Load() which requires a SQLite DB, so mock verification
    /// is covered by integration tests.
    /// </summary>
    [Fact]
    public void Constructor_WithMockedLocalizationManager_DoesNotThrow()
    {
        var mockLocalization = new Mock<ILocalizationManager>();

        var manager = new FactionManager(mockLocalization.Object);

        Assert.NotNull(manager);
        mockLocalization.VerifyNoOtherCalls();
    }
}
