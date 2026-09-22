using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class IndunManagerTests
{
    [Test]
    public async Task RequestLeaveInstance_RejectsCharacterOutsideCurrentDungeon()
    {
        var manager = new IndunManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<ITeamManager>().Object);

        var result = manager.RequestLeaveInstance(new AAEmu.Game.Models.Game.Char.Character(new UnitCustomModelParams()));

        await Assert.That(result).IsFalse();
    }

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
