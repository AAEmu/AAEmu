using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// BubbleEffect.Apply used to end with Thread.Sleep(readTime): the packet went out and then the effect
/// thread and the cast's EndSkill waited out the whole read time. Apply now sends the bubble, hands the
/// read window to the task scheduler and returns.
/// </summary>
[NotInParallel]
public class BubbleEffectTests
{
    private const uint BubbleId = 4242;
    private const uint TargetObjId = 77;

    private SingletonScope<LocalizationManager> _localization;
    private SingletonScope<TaskManager> _tasks;
    private TaskManager _taskManager;

    [Before(Test)]
    public void Setup()
    {
        _localization = new SingletonScope<LocalizationManager>(new LocalizationManager());

        // A real, never started task manager: whatever is scheduled stays queued, which is exactly what
        // the old sleep hid (the effect only returned once the read time had passed).
        _taskManager = new TaskManager(Mock.Of<ITickManager>().Object);
        _tasks = new SingletonScope<TaskManager>(_taskManager);
    }

    [After(Test)]
    public void Teardown()
    {
        _tasks.Dispose();
        _localization.Dispose();
    }

    [Test]
    public async Task Apply_WithNoLocalizedLine_SendsTheBubbleAndSchedulesTheFallbackWindow()
    {
        var target = new RecordingUnit { ObjId = 5 };
        var started = Stopwatch.GetTimestamp();

        new BubbleEffect { Id = BubbleId, KindId = 1 }.Apply(null, null, target,
            new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);
        var elapsed = Stopwatch.GetElapsedTime(started);

        // 2.5 s of read time used to be spent inside this call.
        await Assert.That(elapsed.TotalMilliseconds).IsLessThan(500);

        // The bubble itself is still sent on the spot.
        await Assert.That(target.Broadcasts.Count).IsEqualTo(1);
        await Assert.That(target.Broadcasts[0].Packet).IsTypeOf<SCChatBubblePacket>();
        await Assert.That(target.Broadcasts[0].Self).IsTrue();

        var scheduled = SingleScheduledTask();
        await Assert.That(scheduled).IsTypeOf<BubbleReadTimeTask>();
        var readWindow = (BubbleReadTimeTask)scheduled;
        await Assert.That(readWindow.BubbleId).IsEqualTo(BubbleId);
        await Assert.That(readWindow.TargetObjId).IsEqualTo(TargetObjId);
        await Assert.That(readWindow.ReadTimeMilliseconds)
            .IsEqualTo(BubbleReadTimeRules.NoTextReadTimeMilliseconds);

        // Handed over, not run inline, and scheduled for the read time it reports.
        await Assert.That(scheduled.ExecuteCount).IsEqualTo(0);
        await Assert.That((scheduled.TriggerTime - DateTime.UtcNow).TotalMilliseconds).IsGreaterThan(2000);
    }

    [Test]
    public async Task Apply_WithALocalizedLine_SchedulesTheWindowScaledToTheLine()
    {
        // 90 000 characters * 0.015 = 1350 ms, above the 1250 ms floor.
        LocalizationManager.Instance.AddTranslation("bubble_effects", "speech", BubbleId, new string('x', 90_000));

        new BubbleEffect { Id = BubbleId, KindId = 1 }.Apply(null, null, null,
            new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);

        var readWindow = (BubbleReadTimeTask)SingleScheduledTask();
        await Assert.That(readWindow.ReadTimeMilliseconds).IsEqualTo(1350);
        await Assert.That((readWindow.TriggerTime - DateTime.UtcNow).TotalMilliseconds).IsGreaterThan(1200);
    }

    [Test]
    public async Task Apply_TwiceInARow_DoesNotSerialiseTheTwoBubbles()
    {
        var effect = new BubbleEffect { Id = BubbleId, KindId = 1 };
        var started = Stopwatch.GetTimestamp();

        // A dialogue chain applies its bubbles back to back; neither may wait for the other's window.
        effect.Apply(null, null, null, new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);
        effect.Apply(null, null, null, new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);

        await Assert.That(Stopwatch.GetElapsedTime(started).TotalMilliseconds).IsLessThan(500);
        await Assert.That(_taskManager.GetQueueCount()).IsEqualTo(2);
    }

    private AAEmu.Game.Models.Tasks.Task SingleScheduledTask()
    {
        var queue = (ConcurrentDictionary<uint, AAEmu.Game.Models.Tasks.Task>)typeof(TaskManager)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_taskManager)!;
        return queue.Values.Single();
    }

    /// <summary>Collects packets instead of walking the region grid, which needs a live world.</summary>
    private sealed class RecordingUnit : BaseUnit
    {
        public List<(GamePacket Packet, bool Self)> Broadcasts { get; } = [];

        public override void BroadcastPacket(GamePacket packet, bool self) => Broadcasts.Add((packet, self));
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }
}
