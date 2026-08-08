using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class SpecialtyManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDependencies()
    {
        var itemManager = Mock.Of<IItemManager>();

        var manager = new SpecialtyManager(itemManager.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(itemManager);
    }
}
