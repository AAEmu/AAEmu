using AAEmu.Game.GameData;
using Xunit;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Tests for NpcGameData class
/// </summary>
public class NpcGameDataTests
{
    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = NpcGameData.Instance;
        var instance2 = NpcGameData.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Instance_IsNotNull()
    {
        // Arrange & Act
        var instance = NpcGameData.Instance;

        // Assert
        Assert.NotNull(instance);
    }
}
