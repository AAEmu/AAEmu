using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using Microsoft.Extensions.DependencyInjection;
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
        var startTime = GameService.StartTime;

        // Act
        var timeSinceStart = GameService.TimeSinceStart;

        // Assert
        // Verify TimeSinceStart is non-negative
        Assert.True(timeSinceStart >= TimeSpan.Zero);

        // Verify TimeSinceStart is consistent with the formula: DateTime.UtcNow - StartTime
        // Allow 100ms tolerance for execution time variation
        var expectedTimeSinceStart = DateTime.UtcNow - startTime;
        var tolerance = TimeSpan.FromMilliseconds(100);
        Assert.True(timeSinceStart <= expectedTimeSinceStart + tolerance);
        Assert.True(timeSinceStart >= expectedTimeSinceStart - tolerance);
    }

    [Fact]
    public void GameService_ImplementsIHostedService()
    {
        // Arrange
        var sp = Moq.Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator);

        // Assert
        Assert.IsAssignableFrom<IHostedService>(service);
    }

    [Fact]
    public void GameService_ImplementsIDisposable()
    {
        // Arrange
        var sp = Moq.Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator);

        // Assert
        Assert.IsAssignableFrom<IDisposable>(service);
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        // Arrange
        var sp = Moq.Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator);

        // Act & Assert
        service.Dispose();
        await Task.CompletedTask; // Suppress warning
    }
}
