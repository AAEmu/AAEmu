using AAEmu.Game;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AAEmu.UnitTests.Services;

/// <summary>
/// Tests for GameService class
/// </summary>
public class GameServiceTests
{
    [Fact]
    public void StartTime_IsInitializedToUtcNow()
    {
        // Arrange & Act
        var startTime = GameService.StartTime;

        // Assert
        Assert.True(startTime <= DateTime.UtcNow);
        Assert.True((DateTime.UtcNow - startTime).TotalSeconds < 1);
    }

    [Fact]
    public void TimeSinceStart_ReturnsTimeSpanSinceStart()
    {
        // Arrange
        // Calling StartTime here to make sure the GameService static class is always initialized correctly
        _ = GameService.StartTime;
        var beforeTime = DateTime.UtcNow;

        // Act
        var timeSinceStart = GameService.TimeSinceStart;

        // Assert
        Assert.True(timeSinceStart >= TimeSpan.Zero);
        var expectedMax = DateTime.UtcNow.Subtract(beforeTime);
        Assert.True(timeSinceStart <= expectedMax.Add(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void GameService_ImplementsIHostedService()
    {
        // Arrange
        using var service = new GameService();

        // Assert
        Assert.IsAssignableFrom<IHostedService>(service);
    }

    [Fact]
    public void GameService_ImplementsIDisposable()
    {
        // Arrange
        using var service = new GameService();

        // Assert
        Assert.IsAssignableFrom<IDisposable>(service);
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        // Arrange
        using var service = new GameService();

        // Act & Assert
        service.Dispose();
        await Task.CompletedTask; // Suppress warning
    }
}
