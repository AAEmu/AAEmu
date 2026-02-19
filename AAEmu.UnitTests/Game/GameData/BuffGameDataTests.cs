using AAEmu.Game.GameData;
using Xunit;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for BuffGameData class
/// </summary>
public class BuffGameDataTests
{
    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = BuffGameData.Instance;
        var instance2 = BuffGameData.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = BuffGameData.Instance;

        // Assert
        Assert.NotNull(instance);
    }
}
