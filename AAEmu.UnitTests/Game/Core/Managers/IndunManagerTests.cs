using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class IndunManagerTests
{
    [Test]
    public void Initialize_SubscribesToTickManager()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());
        var manager = new IndunManager(mockTick.Object, Mock.Of<IWorldManager>().Object, Mock.Of<IZoneManager>().Object, Mock.Of<ITeamManager>().Object);
        manager.Initialize();

        mockTick.OnTick.WasCalled(Times.Once);
    }

    [Test]
    public async Task IsDungeonFull_AtMaxCapacity_ReturnsTrue()
    {
        var method = typeof(IndunManager).GetMethod("IsDungeonFull",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        var result = method!.Invoke(null, [1, 1u]);

        await Assert.That(result).IsEqualTo(true);
    }

    [Test]
    public async Task IsDungeonFull_BelowMaxCapacity_ReturnsFalse()
    {
        var method = typeof(IndunManager).GetMethod("IsDungeonFull",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        var result = method!.Invoke(null, [0, 1u]);

        await Assert.That(result).IsEqualTo(false);
    }
}
