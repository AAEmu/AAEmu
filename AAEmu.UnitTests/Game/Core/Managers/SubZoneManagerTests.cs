using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SubZoneManagerTests
{
    [Test]
    public void Load_CallsGetWorlds()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        mockWorld.GetWorlds().Returns([]);
        var manager = new SubZoneManager(mockWorld.Object, Mock.Of<IZoneManager>().Object);
        manager.Load();

        mockWorld.GetWorlds().WasCalled(Times.Once);
    }
}