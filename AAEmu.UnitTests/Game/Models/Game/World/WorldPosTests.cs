using Xunit;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class WorldPosTests
{
    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var pos = new WorldPos(100, 200, 50.5f);

        // Assert
        Assert.Equal(100, pos.X);
        Assert.Equal(200, pos.Y);
        Assert.Equal(50.5f, pos.Z);
    }

    [Fact]
    public void DefaultConstructor_ShouldInitializeToZero()
    {
        // Arrange & Act
        var pos = new WorldPos();

        // Assert
        Assert.Equal(0, pos.X);
        Assert.Equal(0, pos.Y);
        Assert.Equal(0f, pos.Z);
    }

    [Fact]
    public void Clone_ShouldReturnNewInstanceWithSameValues()
    {
        // Arrange
        var original = new WorldPos(100, 200, 50.5f);

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.X, clone.X);
        Assert.Equal(original.Y, clone.Y);
        Assert.Equal(original.Z, clone.Z);
    }

    [Theory]
    [InlineData(0, 0, 0f)]
    [InlineData(1, 2, 3f)]
    [InlineData(-100, -200, -50.5f)]
    [InlineData(long.MaxValue, long.MinValue, float.MaxValue)]
    public void Clone_ShouldHandleVariousValues(long x, long y, float z)
    {
        // Arrange
        var original = new WorldPos(x, y, z);

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Equal(original.X, clone.X);
        Assert.Equal(original.Y, clone.Y);
        Assert.Equal(original.Z, clone.Z);
    }

    [Fact]
    public void Properties_CanBeModified()
    {
        // Arrange
        var pos = new WorldPos();

        // Act
        pos.X = 10;
        pos.Y = 20;
        pos.Z = 30.5f;

        // Assert
        Assert.Equal(10, pos.X);
        Assert.Equal(20, pos.Y);
        Assert.Equal(30.5f, pos.Z);
    }
}
