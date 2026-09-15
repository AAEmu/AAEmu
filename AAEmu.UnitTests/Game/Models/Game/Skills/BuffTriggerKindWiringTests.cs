using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Every kind the handler subscribes has to fire when its event is raised, and the kinds it schedules
/// have to fire on their schedule. The table at the bottom is the whole point: a new kind that is
/// classified as wired but not bound fails <see cref="EveryWiredKindFiresThroughItsOwnEvent"/>.
/// </summary>
/// <remarks>
/// The raise sites themselves (Unit.DoDie, ZoneSimRelay.HandleUnitFell, SlaveCollisionDamage,
/// DamageEffect, CSRemoveBuffPacket, Buffs.RemoveStealth ...) need a world, a zone or a connection and
/// are covered by the callers of those methods; what is tested here is the binding, through the real
/// <see cref="BuffTriggersHandler.SubscribeEvents"/> on a real <see cref="Buff"/>.
/// </remarks>
[NotInParallel]
public class BuffTriggerKindWiringTests
{
    private const uint BuffId = 93001;
    private const uint RequiredBuffId = 93002;

    private SingletonScope<SkillManager> _skills;
    private SingletonScope<BuffGameData> _buffGameData;
    private SingletonScope<TaskManager> _tasks;
    private SingletonScope<EffectTaskManager> _effectTasks;

    [Before(Test)]
    public void InstallContentLookups()
    {
        var taskManager = new TaskManager(Mock.Of<ITickManager>().Object);
        _skills = new SingletonScope<SkillManager>(CreateSkillManager());
        _buffGameData = new SingletonScope<BuffGameData>(CreateBuffGameData());
        _tasks = new SingletonScope<TaskManager>(taskManager);
        _effectTasks = new SingletonScope<EffectTaskManager>(new EffectTaskManager(taskManager));
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _effectTasks.Dispose();
        _tasks.Dispose();
        _buffGameData.Dispose();
        _skills.Dispose();
    }

    #region Every wired kind fires

    /// <summary>
    /// One raise per event-bound kind, with the args its own raiser builds.
    /// </summary>
    private static readonly (BuffEventTriggerKind Kind, Action<Fixture> Raise)[] EventRaisers =
    [
        (BuffEventTriggerKind.Attack, f => f.Owner.Events.OnAttack(f.Owner, new OnAttackArgs { Attacker = f.Owner, Target = f.Other })),
        (BuffEventTriggerKind.Attacked, f => f.Owner.Events.OnAttacked(f.Owner, new OnAttackedArgs { Attacker = f.Other })),
        (BuffEventTriggerKind.Damage, f => f.Owner.Events.OnDamage(f.Owner, new OnDamageArgs { Attacker = f.Owner, Amount = 11, Target = f.Other })),
        (BuffEventTriggerKind.Damaged, f => f.Owner.Events.OnDamaged(f.Owner, new OnDamagedArgs { Attacker = f.Other, Amount = 11 })),
        (BuffEventTriggerKind.Dispelled, f => f.Buff.Events.OnDispelled(f.Buff, new OnDispelledArgs())),
        (BuffEventTriggerKind.Timeout, f => f.Buff.Events.OnTimeout(f.Buff, new OnTimeoutArgs())),
        (BuffEventTriggerKind.DamagedMelee, f => f.Owner.Events.OnDamagedMelee(f.Owner, new OnDamagedArgs { Attacker = f.Other, Amount = 11 })),
        (BuffEventTriggerKind.DamagedRanged, f => f.Owner.Events.OnDamagedRanged(f.Owner, new OnDamagedArgs { Attacker = f.Other, Amount = 11 })),
        (BuffEventTriggerKind.DamagedSpell, f => f.Owner.Events.OnDamagedSpell(f.Owner, new OnDamagedArgs { Attacker = f.Other, Amount = 11 })),
        (BuffEventTriggerKind.DamagedSiege, f => f.Owner.Events.OnDamagedSiege(f.Owner, new OnDamagedArgs { Attacker = f.Other, Amount = 11 })),
        (BuffEventTriggerKind.DamageMelee, f => f.Owner.Events.OnDamageMelee(f.Owner, new OnDamageArgs { Attacker = f.Owner, Amount = 11, Target = f.Other })),
        (BuffEventTriggerKind.DamageRanged, f => f.Owner.Events.OnDamageRanged(f.Owner, new OnDamageArgs { Attacker = f.Owner, Amount = 11, Target = f.Other })),
        (BuffEventTriggerKind.DamageSpell, f => f.Owner.Events.OnDamageSpell(f.Owner, new OnDamageArgs { Attacker = f.Owner, Amount = 11, Target = f.Other })),
        (BuffEventTriggerKind.DamageSiege, f => f.Owner.Events.OnDamageSiege(f.Owner, new OnDamageArgs { Attacker = f.Owner, Amount = 11, Target = f.Other })),
        (BuffEventTriggerKind.Landing, f => f.Owner.Events.OnLanding(f.Owner, new OnLandingArgs())),
        (BuffEventTriggerKind.Started, f => f.Buff.Events.OnBuffStarted(f.Buff, new OnBuffStartedArgs())),
        (BuffEventTriggerKind.RemoveOnMove, f => f.Owner.Events.OnMovement(f.Owner, new OnMovementArgs())),
        (BuffEventTriggerKind.ChannelingCancel, f => f.Owner.Events.OnChannelingCancel(f.Owner, new OnChannelingCancelArgs())),
        (BuffEventTriggerKind.RemoveOnDamaged, f => f.Owner.Events.OnDamaged(f.Owner, new OnDamagedArgs { Attacker = f.Other, Amount = 11 })),
        (BuffEventTriggerKind.Death, f => f.Owner.Events.OnDeath(f.Owner, new OnDeathArgs { Killer = f.Other, Victim = f.Owner })),
        (BuffEventTriggerKind.Unmount, f => f.Owner.Events.OnUnmount(f.Owner, new OnUnmountArgs())),
        (BuffEventTriggerKind.Kill, f => f.Owner.Events.OnKill(f.Owner, new OnKillArgs { Target = f.Other, Killer = f.Owner, Victim = f.Other })),
        (BuffEventTriggerKind.DamagedCollision, f => f.Owner.Events.OnDamagedCollision(f.Owner, new OnDamagedCollisionArgs { Amount = 7, Impact = 12f })),
        (BuffEventTriggerKind.KillAny, f => f.Owner.Events.OnKill(f.Owner, new OnKillArgs { Target = f.Other, Killer = f.Owner, Victim = f.Other })),
        (BuffEventTriggerKind.Any, f => f.Buff.Events.OnDispelled(f.Buff, new OnDispelledArgs())),
        (BuffEventTriggerKind.RemoveNeedBuff, f => f.Buff.Events.OnRequiredBuffLost(f.Buff, new OnRequiredBuffLostArgs { RequiredBuffId = RequiredBuffId })),
        (BuffEventTriggerKind.UserCancel, f => f.Buff.Events.OnUserCancel(f.Buff, new OnUserCancelArgs())),
        (BuffEventTriggerKind.UseSkill, f => f.Owner.Events.OnSkillUse(f.Owner, new OnSkillUseArgs { Skill = null })),
        (BuffEventTriggerKind.RemoveStealth, f => f.Buff.Events.OnStealthRemoved(f.Buff, new OnStealthRemovedArgs())),
        (BuffEventTriggerKind.Absorption, f => f.Buff.Events.OnAbsorptionConsumed(f.Buff, new OnAbsorptionConsumedArgs { Amount = 5 }))
    ];

    [Test]
    public async Task EveryWiredKindFiresThroughItsOwnEvent()
    {
        foreach (var (kind, raise) in EventRaisers)
        {
            var effect = new RecordingEffect();
            var fixture = Setup([Row(kind, effect)]);

            // `started` is raised by Buffs.AddBuff as soon as the rows are subscribed, so it has already
            // fired once before this test raises anything.
            var fired = effect.Applications.Count;
            if (kind == BuffEventTriggerKind.Started)
                await Assert.That(fired).IsEqualTo(1);

            raise(fixture);

            await Assert.That(effect.Applications.Count).IsEqualTo(fired + 1);
        }
    }

    /// <summary>
    /// The exhaustiveness half: every kind in the table is either not applicable, the scheduled one, or
    /// in the raise table above. A kind added to <see cref="BuffTriggerKindRules"/> lands here first.
    /// </summary>
    [Test]
    public async Task TheRaiseTableCoversEveryWiredKind()
    {
        var eventKinds = EventRaisers.Select(entry => entry.Kind).ToHashSet();

        foreach (var binding in BuffTriggerKindRules.All)
        {
            if (binding.Wiring == BuffTriggerWiring.NotApplicable)
                continue;

            if (binding.Kind == BuffEventTriggerKind.Time)
                continue; // scheduled: covered by the TimeTrigger tests below

            await Assert.That(eventKinds.Contains(binding.Kind)).IsTrue()
                .Because($"kind {binding.DbId} ({binding.DbName}) is {binding.Wiring} but has no raise in this test");
        }

        await Assert.That(eventKinds.Count).IsEqualTo(EventRaisers.Length);
    }

    #endregion

    #region The kinds the task named

    [Test]
    public async Task KillTrigger_FiresOnTheKillersEventAndActsOnTheVictimItNames()
    {
        var victim = new RecordingEffect();
        var fixture = Setup([
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Kill,
                Effect = victim,
                // The content authors this on the kill rows that act on what was killed (buff 15099).
                TargetAgentId = BuffTriggerAgent.Target
            }
        ]);

        fixture.Owner.Events.OnKill(fixture.Owner,
            new OnKillArgs { Target = fixture.Other, Killer = fixture.Owner, Victim = fixture.Other });

        await Assert.That(victim.Applications.Count).IsEqualTo(1);
        await Assert.That(victim.Applications[0].Target).IsSameReferenceAs(fixture.Other);
        await Assert.That(victim.Applications[0].Source).IsSameReferenceAs(fixture.Owner);
    }

    [Test]
    public async Task KillAndKillAnyTriggers_BothHangOffTheKillersOnKill()
    {
        var kill = new RecordingEffect();
        var killAny = new RecordingEffect();
        var fixture = Setup([
            Row(BuffEventTriggerKind.Kill, kill),
            Row(BuffEventTriggerKind.KillAny, killAny)
        ]);

        fixture.Owner.Events.OnKill(fixture.Owner,
            new OnKillArgs { Target = fixture.Other, Killer = fixture.Owner, Victim = fixture.Other });

        await Assert.That(kill.Applications.Count).IsEqualTo(1);
        await Assert.That(killAny.Applications.Count).IsEqualTo(1);

        // ...and the victim's own death does not run the killer's rows.
        fixture.Other.Events.OnDeath(fixture.Other, new OnDeathArgs { Killer = fixture.Owner, Victim = fixture.Other });
        await Assert.That(kill.Applications.Count).IsEqualTo(1);
        await Assert.That(killAny.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DeathTrigger_FiresOnTheVictimsOwnDeathAndNotTheKillers()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Death,
                Effect = effect,
                // The content authors target_agent_id = 3 (original_source) to act on the killer.
                TargetAgentId = BuffTriggerAgent.OriginalSource
            }
        ]);

        fixture.Other.Events.OnDeath(fixture.Other, new OnDeathArgs { Killer = fixture.Owner, Victim = fixture.Other });
        await Assert.That(effect.Applications).IsEmpty();

        fixture.Owner.Events.OnDeath(fixture.Owner, new OnDeathArgs { Killer = fixture.Other, Victim = fixture.Owner });
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
        await Assert.That(effect.Applications[0].Target).IsSameReferenceAs(fixture.Caster);
    }

    [Test]
    public async Task UnmountTrigger_FiresOnTheRiderThatDismounts()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.Unmount, effect)]);

        // The raiser is the rider's own event (MateManager.UnMountMate, SlaveManager.UnbindSlave), so
        // another unit dismounting must not run this buff's row.
        fixture.Other.Events.OnUnmount(fixture.Other, new OnUnmountArgs());
        await Assert.That(effect.Applications).IsEmpty();

        fixture.Owner.Events.OnUnmount(fixture.Owner, new OnUnmountArgs());
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ChannelingCancelTrigger_FiresOnTheOwnersChannelingCancel()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.ChannelingCancel, effect)]);

        fixture.Owner.Events.OnChannelingCancel(fixture.Owner, new OnChannelingCancelArgs());
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LandingTrigger_FiresOnTheOwnersLandingAndNotOnADamagingLandingOnly()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.Landing, effect)]);

        fixture.Owner.Events.OnLanding(fixture.Owner, new OnLandingArgs());
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UseSkillTrigger_FiresWhenTheOwnersSkillEnds()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.UseSkill, effect)]);

        // Unit.OnSkillEnd is the raise site, called by Skill.EndSkill and Skill.Stop.
        fixture.Owner.OnSkillEnd(null);

        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveOnMoveTrigger_FiresOnTheOwnersMovement()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.RemoveOnMove, effect)]);

        // Raised where a move is accepted: CSMoveUnitPacket.RemoveEffects for a movement packet, and
        // Unit.SetPosition/CheckMovedPosition for everything the world moves itself. Both need a world
        // (GameObject.SetPosition reads WorldManager), so the binding is what is tested here.
        fixture.Other.Events.OnMovement(fixture.Other, new OnMovementArgs());
        await Assert.That(effect.Applications).IsEmpty();

        fixture.Owner.Events.OnMovement(fixture.Owner, new OnMovementArgs());
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    #endregion

    #region Removal reasons

    [Test]
    public async Task AnyTrigger_FiresOnANaturalTimeoutAndOnAnExplicitRemoval()
    {
        var timedOut = new RecordingEffect();
        var removed = new RecordingEffect();
        var timeoutFixture = Setup([Row(BuffEventTriggerKind.Any, timedOut)]);
        var removeFixture = Setup([Row(BuffEventTriggerKind.Any, removed)]);

        timeoutFixture.Buff.TimeOut();
        await Assert.That(timedOut.Applications.Count).IsEqualTo(1);

        removeFixture.Owner.Buffs.RemoveBuff(BuffId, notifyZone: false);
        await Assert.That(removed.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UserCancelTrigger_IsNotAnOrdinaryDispelAndFiresOnlyOnTheCancelRaise()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.UserCancel, effect)]);

        // Buff.Exit() alone - a dispel, a remove-on flag, a charge running out - is not a player cancel.
        fixture.Buff.Exit();
        await Assert.That(effect.Applications).IsEmpty();

        var cancelling = Setup([Row(BuffEventTriggerKind.UserCancel, effect)]);
        // What CSRemoveBuffPacket does with the client's cancel request, before buff.Exit().
        cancelling.Buff.Events.OnUserCancel(cancelling.Buff, new OnUserCancelArgs());
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveStealthTrigger_FiresWhenTheOwnersStealthIsRemoved()
    {
        var effect = new RecordingEffect();
        var owner = new Unit { ObjId = 1 };
        var caster = new Unit { ObjId = 2 };
        var stealthTemplate = new BuffTemplate { Id = BuffId, Duration = 0, Stealth = true };
        var stealth = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId), stealthTemplate, null, DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        };
        SetField(SkillManager.Instance, "_buffTriggers", new Dictionary<uint, List<BuffTriggerTemplate>>
        {
            [BuffId] = [Row(BuffEventTriggerKind.RemoveStealth, effect)]
        });
        owner.Buffs.AddBuff(stealth);

        owner.Buffs.RemoveStealth();

        await Assert.That(effect.Applications.Count).IsEqualTo(1);
        await Assert.That(owner.Buffs.CheckBuff(BuffId)).IsFalse();
    }

    [Test]
    public async Task AbsorptionTrigger_FiresWhenTheLastChargeIsConsumed()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.Absorption, effect)]);
        fixture.Buff.Charge = 10;

        // Partially consumed: the shield is still up, nothing fires.
        fixture.Buff.ConsumeCharge(4);
        await Assert.That(effect.Applications).IsEmpty();

        fixture.Buff.ConsumeCharge(10);
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveNeedBuffTrigger_FiresWhenTheBuffItRequiresIsRemoved()
    {
        var effect = new RecordingEffect();
        var owner = new Unit { ObjId = 1 };
        var caster = new Unit { ObjId = 2 };
        SetField(SkillManager.Instance, "_buffTriggers", new Dictionary<uint, List<BuffTriggerTemplate>>
        {
            [BuffId] = [Row(BuffEventTriggerKind.RemoveNeedBuff, effect)]
        });

        var requiring = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId),
            new BuffTemplate { Id = BuffId, Duration = 0, RequireBuffId = RequiredBuffId }, null, DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        };
        var required = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId),
            new BuffTemplate { Id = RequiredBuffId, Duration = 0 }, null, DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        };
        owner.Buffs.AddBuff(requiring);
        owner.Buffs.AddBuff(required);

        owner.Buffs.RemoveBuff(RequiredBuffId, notifyZone: false);

        await Assert.That(effect.Applications.Count).IsEqualTo(1);
        await Assert.That(owner.Buffs.CheckBuff(BuffId)).IsFalse();
    }

    #endregion

    #region time

    [Test]
    public async Task TimeTrigger_FiresAtItsOffsetAndNotBefore_AndTheNextRowAtTheNextOffset()
    {
        var first = new RecordingEffect();
        var second = new RecordingEffect();
        var before = DateTime.UtcNow;
        Setup([
            Row(BuffEventTriggerKind.Time, first, delayTime: 300),
            Row(BuffEventTriggerKind.Time, second, delayTime: 700)
        ]);

        // Nothing fires when the buff is applied: buff 25106 carries six such rows over a 180 s buff.
        await Assert.That(first.Applications).IsEmpty();
        await Assert.That(second.Applications).IsEmpty();

        var queued = QueuedTriggerTasks();
        await Assert.That(queued.Count).IsEqualTo(2);
        await Assert.That(queued[0].TriggerTime - before >= TimeSpan.FromMilliseconds(250)).IsTrue();
        await Assert.That(queued[1].TriggerTime - before >= TimeSpan.FromMilliseconds(650)).IsTrue();
        await Assert.That(queued[1].TriggerTime > queued[0].TriggerTime).IsTrue();

        queued[0].Execute();
        await Assert.That(first.Applications.Count).IsEqualTo(1);
        await Assert.That(second.Applications).IsEmpty();

        queued[1].Execute();
        await Assert.That(second.Applications.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TimeTrigger_WithANegativeDelayFiresThatFarBeforeTheBuffEnds()
    {
        var effect = new RecordingEffect();
        var before = DateTime.UtcNow;

        // buff 27016 (운수 좋은 날): duration 23000, delay_time -3000 -> 20 s after the buff starts.
        Setup([Row(BuffEventTriggerKind.Time, effect, delayTime: -3000)], durationMs: 23000);

        var queued = QueuedTriggerTasks();
        await Assert.That(queued.Count).IsEqualTo(1);
        var offset = queued[0].TriggerTime - before;
        await Assert.That(offset >= TimeSpan.FromMilliseconds(19500)).IsTrue();
        await Assert.That(offset <= TimeSpan.FromMilliseconds(20500)).IsTrue();
    }

    [Test]
    public async Task TimeTrigger_IsDroppedWhenTheBuffEndsBeforeItsOffset()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.Time, effect, delayTime: 60000)]);
        await Assert.That(QueuedTriggerTasks().Count).IsEqualTo(1);

        fixture.Buff.TimeOut();

        await Assert.That(QueuedTriggerTasks()).IsEmpty();
        await Assert.That(effect.Applications).IsEmpty();
    }

    /// <summary>
    /// A buff-lifecycle delay is not touched: buff_triggers has 23 enabled `timeout` rows with
    /// delay_time &gt; 0, and their effect is meant to run after the buff ended.
    /// </summary>
    [Test]
    public async Task DelayedTimeoutTrigger_SurvivesTheBuffEnding()
    {
        var effect = new RecordingEffect();
        var fixture = Setup([Row(BuffEventTriggerKind.Timeout, effect, delayTime: 2500)]);

        fixture.Buff.TimeOut();

        var queued = QueuedTriggerTasks();
        await Assert.That(queued.Count).IsEqualTo(1);
        queued[0].Execute();
        await Assert.That(effect.Applications.Count).IsEqualTo(1);
    }

    #endregion

    #region Fixture

    private static BuffTriggerTemplate Row(BuffEventTriggerKind kind, RecordingEffect effect, int delayTime = 0) =>
        new() { Kind = kind, Effect = effect, DelayTime = delayTime };

    /// <summary>Applies the buff under test to a fresh owner, with the rows the caller authored.</summary>
    private static Fixture Setup(BuffTriggerTemplate[] rows, int durationMs = 0)
    {
        var owner = new Unit { ObjId = 1 };
        var caster = new Unit { ObjId = 2 };
        var other = new Unit { ObjId = 3 };

        SetField(SkillManager.Instance, "_buffTriggers",
            new Dictionary<uint, List<BuffTriggerTemplate>> { [BuffId] = [.. rows] });

        var buff = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId),
            new BuffTemplate { Id = BuffId, Duration = durationMs }, null, DateTime.UtcNow)
        {
            Passive = true, // keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test
            AbLevel = 1
        };

        owner.Buffs.AddBuff(buff);
        return new Fixture { Owner = owner, Caster = caster, Other = other, Buff = buff };
    }

    private sealed class Fixture
    {
        public Unit Owner { get; init; }
        public Unit Caster { get; init; }
        public Unit Other { get; init; }
        public Buff Buff { get; init; }
    }

    /// <summary>The queued trigger applications, oldest first.</summary>
    private static List<BuffTriggerTask> QueuedTriggerTasks()
    {
        var queueField = typeof(TaskManager).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = (ConcurrentDictionary<uint, GameTask>)queueField.GetValue(TaskManager.Instance)!;
        return [.. queue.Values.OfType<BuffTriggerTask>().OrderBy(task => task.TriggerTime)];
    }

    private static SkillManager CreateSkillManager()
    {
        var manager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(manager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [BuffId] = new BuffTemplate { Id = BuffId, Duration = 0 },
            [RequiredBuffId] = new BuffTemplate { Id = RequiredBuffId, Duration = 0 }
        });
        SetField(manager, "_taggedBuffs", new Dictionary<uint, List<uint>>());
        return manager;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    /// <summary>Adding a buff reads buff_modifiers; with no content loaded they must come back empty
    /// rather than from a null table.</summary>
    private static BuffGameData CreateBuffGameData()
    {
        var gameData = new BuffGameData();
        SetField(gameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(gameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(gameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        return gameData;
    }

    /// <summary>Records what a fired trigger applied, so the test can read source, target and amount.</summary>
    private sealed class RecordingEffect : EffectTemplate
    {
        public List<(BaseUnit Source, BaseUnit Target, int Amount)> Applications { get; } = [];

        public override bool OnActionTime => false;

        public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null) =>
            Applications.Add((caster, target, source?.Amount ?? 0));
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(AAEmu.Commons.Utils.Singleton<T>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }

    #endregion
}
