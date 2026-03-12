
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class WorldPosTests
{
    [Test]
    public async Task Constructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var pos = new WorldPos(100, 200, 50.5f);

        // Assert
        await Assert.That(pos.X).IsEqualTo(100);
        await Assert.That(pos.Y).IsEqualTo(200);
        await Assert.That(pos.Z).IsEqualTo(50.5f);
    }

    [Test]
    public async Task DefaultConstructor_ShouldInitializeToZero()
    {
        // Arrange & Act
        var pos = new WorldPos();

        // Assert
        await Assert.That(pos.X).IsEqualTo(0);
        await Assert.That(pos.Y).IsEqualTo(0);
        await Assert.That(pos.Z).IsEqualTo(0f);
    }

    [Test]
    public async Task Clone_ShouldReturnNewInstanceWithSameValues()
    {
        // Arrange
        var original = new WorldPos(100, 200, 50.5f);

        // Act
        var clone = original.Clone();

        // Assert
        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.X).IsEqualTo(original.X);
        await Assert.That(clone.Y).IsEqualTo(original.Y);
        await Assert.That(clone.Z).IsEqualTo(original.Z);
    }

    [Test]
    [Arguments(0, 0, 0f)]
    [Arguments(1, 2, 3f)]
    [Arguments(-100, -200, -50.5f)]
    [Arguments(long.MaxValue, long.MinValue, float.MaxValue)]
    public async Task Clone_ShouldHandleVariousValues(long x, long y, float z)
    {
        // Arrange
        var original = new WorldPos(x, y, z);

        // Act
        var clone = original.Clone();

        // Assert
        await Assert.That(clone.X).IsEqualTo(original.X);
        await Assert.That(clone.Y).IsEqualTo(original.Y);
        await Assert.That(clone.Z).IsEqualTo(original.Z);
    }

    [Test]
    public async Task Properties_CanBeModified()
    {
        // Arrange
        var pos = new WorldPos();

        // Act
        pos.X = 10;
        pos.Y = 20;
        pos.Z = 30.5f;

        // Assert
        await Assert.That(pos.X).IsEqualTo(10);
        await Assert.That(pos.Y).IsEqualTo(20);
        await Assert.That(pos.Z).IsEqualTo(30.5f);
    }
}