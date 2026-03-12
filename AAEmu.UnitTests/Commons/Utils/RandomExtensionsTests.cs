using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class RandomExtensionsTests
{
    [Test]
    public async Task Next_ShouldReturnValueInRange_WhenGivenValidRange()
    {
        // Arrange
        var random = new Random(12345);
        var minValue = 1.0f;
        var maxValue = 10.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        await Assert.That(result).IsGreaterThanOrEqualTo(minValue).And.IsLessThanOrEqualTo(maxValue);
    }

    [Test]
    public async Task Next_ShouldReturnMinValue_WhenRandomReturnsZero()
    {
        // Arrange
        var random = new Random(0);
        var minValue = 5.0f;
        var maxValue = 5.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        await Assert.That(result).IsEqualTo(5.0f);
    }

    [Test]
    public async Task Next_ShouldReturnValueGreaterThanOrEqualToMinValue()
    {
        // Arrange
        var random = new Random(12345);
        var minValue = 100.0f;
        var maxValue = 200.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        await Assert.That(result >= minValue).IsTrue();
    }

    [Test]
    public async Task Next_ShouldReturnValueLessThanMaxValue()
    {
        // Arrange
        var random = new Random(12345);
        var minValue = 100.0f;
        var maxValue = 200.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        await Assert.That(result < maxValue).IsTrue();
    }

    [Test]
    [Arguments(0f, 1f)]
    [Arguments(-10f, 10f)]
    [Arguments(100f, 200f)]
    [Arguments(0.0f, 0.0001f)]
    public async Task Next_ShouldWorkWithVariousRanges(float minValue, float maxValue)
    {
        // Arrange
        var random = new Random();

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        await Assert.That(result).IsGreaterThanOrEqualTo(minValue).And.IsLessThanOrEqualTo(maxValue);
    }

    [Test]
    public async Task Next_ShouldReturnSameSequence_WhenUsingSameSeed()
    {
        // Arrange
        var random1 = new Random(42);
        var random2 = new Random(42);

        // Act & Assert
        for (var i = 0; i < 10; i++)
        {
            var result1 = random1.Next(0f, 100f);
            var result2 = random2.Next(0f, 100f);
            await Assert.That(result2).IsEqualTo(result1);
        }
    }
}
