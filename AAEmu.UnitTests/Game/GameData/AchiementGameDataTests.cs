using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for AchievementGameData class
/// </summary>
public class AchievementGameDataTests : SqliteTestBase
{
    [Test]
    public async Task CanCreateInstance()
    {
        var instance = new AchievementGameData();
        await Assert.That(instance).IsNotNull();
    }

    [Test]
    public async Task NewInstances_AreIndependent()
    {
        var instance1 = new AchievementGameData();
        var instance2 = new AchievementGameData();
        await Assert.That(instance2).IsNotSameReferenceAs(instance1);
    }
}
