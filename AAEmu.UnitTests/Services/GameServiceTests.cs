using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Moq;
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
        var fakeTime = new FakeTimeProvider();
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, fakeTime);

        Assert.Equal(fakeTime.GetUtcNow().UtcDateTime, GameService.StartTime);
    }

    [Fact]
    public void TimeSinceStart_ReturnsTimeSpanSinceStart()
    {
        var fakeTime = new FakeTimeProvider();
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, fakeTime);

        fakeTime.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), GameService.TimeSinceStart);
    }

    [Fact]
    public void GameService_ImplementsIHostedService()
    {
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, TimeProvider.System);

        Assert.IsAssignableFrom<IHostedService>(service);
    }

    [Fact]
    public void GameService_ImplementsIDisposable()
    {
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, TimeProvider.System);

        Assert.IsAssignableFrom<IDisposable>(service);
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, TimeProvider.System);

        service.Dispose();
        await Task.CompletedTask; // Suppress warning
    }
}
