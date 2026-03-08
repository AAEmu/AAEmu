using Xunit;

using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class RandomExtensionsTests
{
    [Fact]
    public void Next_ShouldReturnValueInRange_WhenGivenValidRange()
    {
        // Arrange
        var random = new Random(12345);
        var minValue = 1.0f;
        var maxValue = 10.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        Assert.InRange(result, minValue, maxValue);
    }

    [Fact]
    public void Next_ShouldReturnMinValue_WhenRandomReturnsZero()
    {
        // Arrange
        var random = new Random(0);
        var minValue = 5.0f;
        var maxValue = 5.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        Assert.Equal(5.0f, result);
    }

    [Fact]
    public void Next_ShouldReturnValueGreaterThanOrEqualToMinValue()
    {
        // Arrange
        var random = new Random(12345);
        var minValue = 100.0f;
        var maxValue = 200.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        Assert.True(result >= minValue);
    }

    [Fact]
    public void Next_ShouldReturnValueLessThanMaxValue()
    {
        // Arrange
        var random = new Random(12345);
        var minValue = 100.0f;
        var maxValue = 200.0f;

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        Assert.True(result < maxValue);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-10f, 10f)]
    [InlineData(100f, 200f)]
    [InlineData(0.0f, 0.0001f)]
    public void Next_ShouldWorkWithVariousRanges(float minValue, float maxValue)
    {
        // Arrange
        var random = new Random();

        // Act
        var result = random.Next(minValue, maxValue);

        // Assert
        Assert.InRange(result, minValue, maxValue);
    }

    [Fact]
    public void Next_ShouldReturnSameSequence_WhenUsingSameSeed()
    {
        // Arrange
        var random1 = new Random(42);
        var random2 = new Random(42);

        // Act & Assert
        for (var i = 0; i < 10; i++)
        {
            var result1 = random1.Next(0f, 100f);
            var result2 = random2.Next(0f, 100f);
            Assert.Equal(result1, result2);
        }
    }
}
