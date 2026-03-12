using AAEmu.Game.Utils;

namespace AAEmu.UnitTests.Game.Utils;

/// <summary>
/// Tests for MathUtil class
/// </summary>
public class MathUtilTests
{
    [Test]
    public async Task CalculateAngleFrom_WithSamePoints_ReturnsZero()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = 0, y2 = 0;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task CalculateAngleFrom_WithPointOnXAxis_ReturnsZero()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = 10, y2 = 0;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task CalculateAngleFrom_WithPointOnYAxis_Returns90Degrees()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = 0, y2 = 10;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        await Assert.That(result).IsEqualTo(90).Within(0.00001f);
    }

    [Test]
    public async Task CalculateAngleFrom_WithNegativeXAxis_Returns180Degrees()
    {
        // Arrange
        const float x1 = 0, y1 = 0;
        const float x2 = -10, y2 = 0;

        // Act
        var result = MathUtil.CalculateAngleFrom(x1, y1, x2, y2);

        // Assert
        await Assert.That(result).IsEqualTo(180).Within(0.00001f);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(90, 32)]
    [Arguments(180, 64)]
    [Arguments(270, -32)]
    [Arguments(359, 0)]
    public async Task ConvertDegreeToSByteDirection_ValidDegrees_ReturnsExpectedDirection(double degree, sbyte expected)
    {
        // Act
        var result = MathUtil.ConvertDegreeToSByteDirection(degree);

        // Assert
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(28, 78.75)]
    [Arguments(56, 157.5)]
    [Arguments(85, 239.0625)]
    [Arguments(113, 317.8125)]
    public async Task ConvertSbyteDirectionToDegree_ValidDirections_ReturnsExpectedDegree(sbyte direction, float expected)
    {
        // Act
        var result = MathUtil.ConvertSbyteDirectionToDegree(direction);

        // Assert
        await Assert.That(result).IsEqualTo(expected).Within(0.00001f);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(1, 57.29578)]
    [Arguments(3.14159, 180)]
    [Arguments(6.28318, 360)]
    public async Task RadToDeg_ValidRadians_ReturnsExpectedDegrees(float radians, float expected)
    {
        // Act
        var result = radians.RadToDeg();

        // Assert
        await Assert.That(result).IsEqualTo(expected).Within(0.005f);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(57.29578, 1)]
    [Arguments(180, 3.14159)]
    [Arguments(360, 6.28318)]
    public async Task DegToRad_ValidDegrees_ReturnsExpectedRadians(float degrees, float expected)
    {
        // Act
        var result = degrees.DegToRad();

        // Assert
        await Assert.That(result).IsEqualTo(expected).Within(0.005f);
    }
}
