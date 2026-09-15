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

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// BubbleEffect.Apply used to end with <c>Thread.Sleep(0.015 * characters)</c> clamped to 1 250 ms, which
/// was a flat 1 250 ms on every shipped line (900 characters per minute is 66.7 ms per character, so the
/// per-character term never reached the floor), and held the effect pipeline and the cast's EndSkill for
/// that long. Apply now sends the bubble and returns, and schedules nothing: there is no read window for
/// anything to consume, since the client retires the bubble on its own.
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

        // A real, never started task manager: anything scheduled would stay queued, so an empty queue is
        // what shows the effect hands nothing over.
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
    public async Task Apply_SendsTheBubbleAndSchedulesNothing()
    {
        var target = new RecordingUnit { ObjId = 5 };
        var started = Stopwatch.GetTimestamp();

        new BubbleEffect { Id = BubbleId, KindId = 1 }.Apply(null, null, target,
            new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);
        var elapsed = Stopwatch.GetElapsedTime(started);

        // 1.25 s of read time used to be spent inside this call.
        await Assert.That(elapsed.TotalMilliseconds).IsLessThan(500);

        // The bubble itself is still sent on the spot.
        await Assert.That(target.Broadcasts.Count).IsEqualTo(1);
        await Assert.That(target.Broadcasts[0].Packet).IsTypeOf<SCChatBubblePacket>();
        await Assert.That(target.Broadcasts[0].Self).IsTrue();

        await Assert.That(ScheduledTasks()).IsEmpty();
    }

    [Test]
    public async Task Apply_WithALocalizedLine_SchedulesNothingEither()
    {
        // The localized line used to scale the window. At the cited 900 characters per minute this one
        // would be held for 17.6 s, and nothing consumes the window, so no task is queued for it.
        LocalizationManager.Instance.AddTranslation("bubble_effects", "speech", BubbleId, new string('x', 264));

        new BubbleEffect { Id = BubbleId, KindId = 1 }.Apply(null, null, null,
            new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);

        await Assert.That(ScheduledTasks()).IsEmpty();
    }

    [Test]
    public async Task Apply_TwiceInARow_DoesNotSerialiseTheTwoBubbles()
    {
        var effect = new BubbleEffect { Id = BubbleId, KindId = 1 };
        var started = Stopwatch.GetTimestamp();

        // A dialogue chain applies its bubbles back to back; neither waits for the other's window.
        effect.Apply(null, null, null, new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);
        effect.Apply(null, null, null, new SkillCastUnitTarget(TargetObjId), null, null, null, DateTime.UtcNow);

        await Assert.That(Stopwatch.GetElapsedTime(started).TotalMilliseconds).IsLessThan(500);
        await Assert.That(ScheduledTasks()).IsEmpty();
        await Assert.That(_taskManager.GetQueueCount()).IsEqualTo(0);
    }

    private List<AAEmu.Game.Models.Tasks.Task> ScheduledTasks()
    {
        var queue = (ConcurrentDictionary<uint, AAEmu.Game.Models.Tasks.Task>)typeof(TaskManager)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_taskManager)!;
        return [.. queue.Values];
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
