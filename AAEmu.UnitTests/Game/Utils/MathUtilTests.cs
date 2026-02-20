using AAEmu.Game.Utils;
using Xunit;

namespace AAEmu.UnitTests.Game.Utils;

/// <summary>
/// Tests for MathUtil class
/// </summary>
public class MathUtilTests
{
    [Fact]
    public void CalculateAngleFrom_WithSamePoints_ReturnsZero()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = 0, y2 = 0;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateAngleFrom_WithPointOnXAxis_ReturnsZero()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = 10, y2 = 0;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateAngleFrom_WithPointOnYAxis_Returns90Degrees()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = 0, y2 = 10;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        Assert.Equal(90, result, 5);
    }

    [Fact]
    public void CalculateAngleFrom_WithNegativeXAxis_Returns180Degrees()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = -10, y2 = 0;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        Assert.Equal(180, result, 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 32)]
    [InlineData(180, 64)]
    [InlineData(270, -32)]
    [InlineData(359, 0)]
    public void ConvertDegreeToSByteDirection_ValidDegrees_ReturnsExpectedDirection(double degree, sbyte expected)
    {
        // Act
        var result = MathUtil.ConvertDegreeToSByteDirection(degree);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(28, 78.75)]
    [InlineData(56, 157.5)]
    [InlineData(85, 239.0625)]
    [InlineData(113, 317.8125)]
    public void ConvertSbyteDirectionToDegree_ValidDirections_ReturnsExpectedDegree(sbyte direction, float expected)
    {
        // Act
        var result = MathUtil.ConvertSbyteDirectionToDegree(direction);

        // Assert
        Assert.Equal(expected, result, 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 57.29578)]
    [InlineData(3.14159, 180)]
    [InlineData(6.28318, 360)]
    public void RadToDeg_ValidRadians_ReturnsExpectedDegrees(float radians, float expected)
    {
        // Act
        var result = radians.RadToDeg();

        // Assert
        Assert.Equal(expected, result, 2);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(57.29578, 1)]
    [InlineData(180, 3.14159)]
    [InlineData(360, 6.28318)]
    public void DegToRad_ValidDegrees_ReturnsExpectedRadians(float degrees, float expected)
    {
        // Act
        var result = degrees.DegToRad();

        // Assert
        Assert.Equal(expected, result, 2);
    }
}
