using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for BuffGameData class
/// </summary>
public class BuffGameDataTests
{
    [Test]
    public async Task CanCreateInstance()
    {
        var instance = new BuffGameData();
        await Assert.That(instance).IsNotNull();
    }

    [Test]
    public async Task NewInstances_AreIndependent()
    {
        var instance1 = new BuffGameData();
        var instance2 = new BuffGameData();
        await Assert.That(instance2).IsNotSameReferenceAs(instance1);
    }
}
