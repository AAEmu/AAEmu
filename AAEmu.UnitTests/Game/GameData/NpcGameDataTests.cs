using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for NpcGameData class
/// </summary>
public class NpcGameDataTests
{
    [Test]
    public async Task CanCreateInstance()
    {
        var instance = new NpcGameData();
        await Assert.That(instance).IsNotNull();
    }

    [Test]
    public async Task NewInstances_AreIndependent()
    {
        var instance1 = new NpcGameData();
        var instance2 = new NpcGameData();
        await Assert.That(instance2).IsNotSameReferenceAs(instance1);
    }
}