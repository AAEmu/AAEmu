
using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class TimeSpanExtensionsTests
{
    [Test]
    public async Task IsBetween_ShouldReturnTrue_WhenTimeIsBetweenStartAndEnd()
    {
        // Arrange
        var time = TimeSpan.FromHours(12);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsBetween_ShouldReturnTrue_WhenTimeEqualsStart()
    {
        // Arrange
        var time = TimeSpan.FromHours(6);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsBetween_ShouldReturnTrue_WhenTimeEqualsEnd()
    {
        // Arrange
        var time = TimeSpan.FromHours(18);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsBetween_ShouldReturnFalse_WhenTimeIsOutsideRange()
    {
        // Arrange
        var time = TimeSpan.FromHours(3);
        var startTime = TimeSpan.FromHours(6);
        var endTime = TimeSpan.FromHours(18);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsBetween_ShouldReturnTrue_WhenEndTimeLessThanStartTimeAndTimeIsInFirstRange()
    {
        // Arrange - overnight scenario: 22:00 to 02:00
        var time = TimeSpan.FromHours(23);
        var startTime = TimeSpan.FromHours(22);
        var endTime = TimeSpan.FromHours(2);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsBetween_ShouldReturnTrue_WhenEndTimeLessThanStartTimeAndTimeIsInSecondRange()
    {
        // Arrange - overnight scenario: 22:00 to 02:00
        var time = TimeSpan.FromHours(1);
        var startTime = TimeSpan.FromHours(22);
        var endTime = TimeSpan.FromHours(2);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsBetween_ShouldReturnFalse_WhenEndTimeLessThanStartTimeAndTimeIsInMiddleGap()
    {
        // Arrange - overnight scenario: 22:00 to 02:00
        var time = TimeSpan.FromHours(12);
        var startTime = TimeSpan.FromHours(22);
        var endTime = TimeSpan.FromHours(2);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsBetween_ShouldReturnTrue_WhenStartTimeEqualsEndTime()
    {
        // Arrange - special case where start == end
        var time = TimeSpan.FromHours(12);
        var startTime = TimeSpan.FromHours(12);
        var endTime = TimeSpan.FromHours(12);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments(0, 0, 10)]
    [Arguments(5, 0, 10)]
    [Arguments(10, 0, 10)]
    public async Task IsBetween_ShouldHandleZeroHours(int hours, int startHours, int endHours)
    {
        // Arrange
        var time = TimeSpan.FromHours(hours);
        var startTime = TimeSpan.FromHours(startHours);
        var endTime = TimeSpan.FromHours(endHours);

        // Act
        var result = time.IsBetween(startTime, endTime);

        // Assert
        await Assert.That(result).IsTrue();
    }
}
