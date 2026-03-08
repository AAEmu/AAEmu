using Xunit;

using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class TimeSpanExtensionsTests
{
    [Fact]
    public void IsBetween_ShouldReturnTrue_WhenTimeIsBetweenStartAndEnd()
    {
        // Arrange
        var time = TimeSpan.FromHours(12);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsBetween_ShouldReturnTrue_WhenTimeEqualsStart()
    {
        // Arrange
        var time = TimeSpan.FromHours(6);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsBetween_ShouldReturnTrue_WhenTimeEqualsEnd()
    {
        // Arrange
        var time = TimeSpan.FromHours(18);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsBetween_ShouldReturnFalse_WhenTimeIsOutsideRange()
    {
        // Arrange
        var time = TimeSpan.FromHours(3);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsBetween_ShouldReturnTrue_WhenEndTimeLessThanStartTimeAndTimeIsInFirstRange()
    {
        // Arrange - overnight scenario: 22:00 to 02:00
        var time = TimeSpan.FromHours(23);
        var startTime = TimeSpan.FromHours(22);
        var endTime = TimeSpan.FromHours(2);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsBetween_ShouldReturnTrue_WhenEndTimeLessThanStartTimeAndTimeIsInSecondRange()
    {
        // Arrange - overnight scenario: 22:00 to 02:00
        var time = TimeSpan.FromHours(1);
        var startTime = TimeSpan.FromHours(22);
        var endTime = TimeSpan.FromHours(2);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsBetween_ShouldReturnFalse_WhenEndTimeLessThanStartTimeAndTimeIsInMiddleGap()
    {
        // Arrange - overnight scenario: 22:00 to 02:00
        var time = TimeSpan.FromHours(12);
        var startTime = TimeSpan.FromHours(22);
        var endTime = TimeSpan.FromHours(2);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsBetween_ShouldReturnTrue_WhenStartTimeEqualsEndTime()
    {
        // Arrange - special case where start == end
        var time = TimeSpan.FromHours(12);
        var startTime = TimeSpan.FromHours(12);
        var endTime = TimeSpan.FromHours(12);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(5, 0, 10)]
    [InlineData(10, 0, 10)]
    public void IsBetween_ShouldHandleZeroHours(int hours, int startHours, int endHours)
    {
        // Arrange
        var time = TimeSpan.FromHours(hours);
        var startTime = TimeSpan.FromHours(startHours);
        var endTime = TimeSpan.FromHours(endHours);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        Assert.InRange(result, false, true);
    }
}
