using System.Diagnostics;

using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TickManagerTests
{
    private static readonly TimeSpan OneMsTick = TimeSpan.FromMilliseconds(1);

    [Test]
    public async Task Invoke_WhenSlowAsyncSubscriber_ReturnsWithoutWaiting()
    {
        var handler = new TickManager.TickEventHandler();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        handler.Subscribe(_ =>
        {
            started.Set();
            release.Wait();
        }, OneMsTick, useAsync: true);

        var sw = Stopwatch.StartNew();
        handler.Invoke();
        sw.Stop();

        await Assert.That(started.Wait(TimeSpan.FromSeconds(2))).IsTrue();
        release.Set();

        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(100);
    }

    [Test]
    public async Task Invoke_WhenSyncSubscriber_BlocksUntilComplete()
    {
        var handler = new TickManager.TickEventHandler();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        handler.Subscribe(_ =>
        {
            entered.Set();
            release.Wait();
        }, OneMsTick, useAsync: false);

        var invokeTask = Task.Run(() => handler.Invoke());
        await Assert.That(entered.Wait(TimeSpan.FromSeconds(2))).IsTrue();
        await Assert.That(invokeTask.IsCompleted).IsFalse();

        release.Set();
        await invokeTask;

        await Assert.That(invokeTask.IsCompleted).IsTrue();
    }

    [Test]
    public async Task Invoke_WhenAsyncSubscriberBusy_DoesNotStartOverlappingRun()
    {
        var handler = new TickManager.TickEventHandler();
        var starts = 0;
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        handler.Subscribe(_ =>
        {
            Interlocked.Increment(ref starts);
            started.Set();
            release.Wait();
        }, OneMsTick, useAsync: true);

        handler.Invoke();
        await Assert.That(started.Wait(TimeSpan.FromSeconds(2))).IsTrue();

        for (var i = 0; i < 20; i++)
            handler.Invoke();

        await Assert.That(starts).IsEqualTo(1);
        release.Set();
    }

    [Test]
    public async Task RepeatedInvoke_WhenSlowNeighborIsAsync_InvokeKeepsReturning()
    {
        var handler = new TickManager.TickEventHandler();
        using var slowStarted = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        handler.Subscribe(_ => { }, OneMsTick, useAsync: true);
        handler.Subscribe(_ =>
        {
            slowStarted.Set();
            release.Wait();
        }, OneMsTick, useAsync: true);

        handler.Invoke();
        await Assert.That(slowStarted.Wait(TimeSpan.FromSeconds(2))).IsTrue();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
            handler.Invoke();
        sw.Stop();

        release.Set();

        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(100);
    }
}
