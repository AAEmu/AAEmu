using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for ItemGameData class
/// </summary>
public class ItemGameDataTests
{
    [Test]
    public async Task CanCreateInstance()
    {
        var instance = new ItemGameData();
        await Assert.That(instance).IsNotNull();
    }

    [Test]
    public async Task NewInstances_AreIndependent()
    {
        var instance1 = new ItemGameData();
        var instance2 = new ItemGameData();
        await Assert.That(instance2).IsNotSameReferenceAs(instance1);
    }
}