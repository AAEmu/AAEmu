using AAEmu.Game.Core.Managers.Id;

namespace AAEmu.UnitTests.Game.Core.Managers.Id;

public class ShipyardIdManagerTests
{
    [Test]
    public async Task Load_UsesTransientIdStore()
    {
        var manager = new ShipyardIdManager();

        manager.Load();

        await Assert.That(manager.GetNextId()).IsEqualTo(1u);
        await Assert.That(manager.GetNextId()).IsEqualTo(2u);
    }
}
