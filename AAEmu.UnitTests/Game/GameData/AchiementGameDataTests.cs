using AAEmu.Game.GameData;
using Xunit;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for AchievementGameData class
/// </summary>
public class AchievementGameDataTests : SqliteTestBase
{
    private readonly AchievementGameData _cut = AchievementGameData.Instance;

    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = AchievementGameData.Instance;
        var instance2 = AchievementGameData.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = AchievementGameData.Instance;

        // Assert
        Assert.NotNull(instance);
    }
}
