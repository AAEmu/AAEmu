using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace AAEmu.UnitTests.Services;

/// <summary>
/// Tests for GameService class
/// </summary>
[NotInParallel]
public class GameServiceTests
{
    [Test]
    public async Task StartTime_IsInitializedToUtcNow()
    {
        var fakeTime = new FakeTimeProvider();
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, fakeTime);

        await Assert.That(GameService.StartTime).IsEqualTo(fakeTime.GetUtcNow().UtcDateTime);
    }

    [Test]
    public async Task TimeSinceStart_ReturnsTimeSpanSinceStart()
    {
        var fakeTime = new FakeTimeProvider();
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, fakeTime);

        fakeTime.Advance(TimeSpan.FromSeconds(5));

        await Assert.That(GameService.TimeSinceStart).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task GameService_ImplementsIHostedService()
    {
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, TimeProvider.System);

        await Assert.That(service).IsAssignableTo<IHostedService>();
    }

    [Test]
    public async Task GameService_ImplementsIDisposable()
    {
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, TimeProvider.System);

        await Assert.That(service).IsAssignableTo<IDisposable>();
    }

    [Test]
    public async Task Dispose_DoesNotThrow()
    {
        var sp = Mock.Of<IServiceProvider>();
        var orchestrator = new ManagerOrchestrator(sp, new ServiceCollection());
        using var service = new GameService(sp, orchestrator, TimeProvider.System);

        service.Dispose();
        await Task.CompletedTask; // Suppress warning
    }
}
