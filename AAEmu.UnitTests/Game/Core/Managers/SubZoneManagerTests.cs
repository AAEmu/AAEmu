using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SubZoneManagerTests
{
    [Test]
    public void Load_CallsGetWorlds()
    {
        var mockWorld = new Mock<IWorldManager>();
        mockWorld.Setup(w => w.GetWorlds()).Returns([]);
        var manager = new SubZoneManager(mockWorld.Object, new Mock<IZoneManager>().Object);
        manager.Load();

        mockWorld.Verify(w => w.GetWorlds(), Times.Once);
    }
}