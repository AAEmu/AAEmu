using AAEmu.Game.GameData;
using Xunit;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for ItemGameData class
/// </summary>
public class ItemGameDataTests
{
    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = ItemGameData.Instance;
        var instance2 = ItemGameData.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = ItemGameData.Instance;

        // Assert
        Assert.NotNull(instance);
    }
}
