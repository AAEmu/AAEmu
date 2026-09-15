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
/// The whole path, not just the decision: a real <see cref="Buff"/> on a real <see cref="Unit"/>, its
/// triggers subscribed by <see cref="BuffTriggersHandler"/>, and the events a real damage hit raises
/// (<c>DamageEffect</c>: <c>caster.Events.OnAttack</c>, <c>trg.Events.OnAttacked</c>,
/// <c>caster.Events.OnDamage</c>, <c>trg.Events.OnDamaged</c>).
/// </summary>
/// <remarks>
/// The regression this covers: a debuff cast by A on B used to subscribe to A's events, so it reacted to
/// A being hit and never to B - and threw outright when A was not a Unit at all.
/// </remarks>
[NotInParallel]
public class BuffTriggerOwnerTests
{
    private const uint DebuffId = 92001;
    private const uint OwnerTagId = 92002;
    private const uint TaggedBuffId = 92003;

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

    #region Owner-centric subscriptions

    [Test]
    public async Task DebuffCastByAOnB_ReactsToBDamagedAndNotToADamaged()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var effect = new RecordingEffect();
        AttachDebuff(victim, applier,
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Damaged, Effect = effect, UseDamageAmount = true });

        // A is damaged. The row belongs to B: before this fix the handler had subscribed it to the
        // applier's events, so this raised the effect with A as both source and target.
        applier.Events.OnDamaged(applier, new OnDamagedArgs { Attacker = victim, Amount = 40 });
        await Assert.That(effect.Applications).IsEmpty();

        // B is damaged by A. This is the event the row was authored for.
        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 40 });

        await Assert.That(effect.Applications).Count().IsEqualTo(1);
        var application = effect.Applications[0];
        await Assert.That(application.Source).IsSameReferenceAs(victim);
        await Assert.That(application.Target).IsSameReferenceAs(victim);
        await Assert.That(application.Amount).IsEqualTo(40);
    }

    [Test]
    public async Task DamagedTrigger_WithSourceAgents_ActsOnTheAttacker()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var effect = new RecordingEffect();
        AttachDebuff(victim, applier, new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.Damaged,
            Effect = effect,
            SourceAgentId = BuffTriggerAgent.Source,
            TargetAgentId = BuffTriggerAgent.Source
        });

        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 12 });

        await Assert.That(effect.Applications).Count().IsEqualTo(1);
        await Assert.That(effect.Applications[0].Source).IsSameReferenceAs(applier);
        await Assert.That(effect.Applications[0].Target).IsSameReferenceAs(applier);
    }

    [Test]
    public async Task AttackAndAttackedTriggers_BindToTheirOwnSideOfTheHit()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var attackEffect = new RecordingEffect();
        var attackedEffect = new RecordingEffect();
        AttachDebuff(victim, applier,
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Attack,
                Effect = attackEffect,
                TargetAgentId = BuffTriggerAgent.Target
            },
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Attacked,
                Effect = attackedEffect,
                SourceAgentId = BuffTriggerAgent.Source
            });

        // A hit B: DamageEffect raises OnAttack on A and OnAttacked on B.
        applier.Events.OnAttack(applier, new OnAttackArgs { Attacker = applier, Target = victim });
        victim.Events.OnAttacked(victim, new OnAttackedArgs { Attacker = applier });

        // "when I attack" is the attacker's own event, so it must not have run for the victim's buff.
        await Assert.That(attackEffect.Applications).IsEmpty();
        await Assert.That(attackedEffect.Applications).Count().IsEqualTo(1);
        await Assert.That(attackedEffect.Applications[0].Source).IsSameReferenceAs(applier);

        // ...and the reverse: an attack by the buff's owner is not this row's event.
        victim.Events.OnAttack(victim, new OnAttackArgs { Attacker = victim, Target = applier });
        await Assert.That(attackEffect.Applications).Count().IsEqualTo(1);
        await Assert.That(attackEffect.Applications[0].Target).IsSameReferenceAs(applier);
    }

    [Test]
    public async Task DebuffOnANonUnitOwner_DoesNotThrowOnSubscribe()
    {
        // buff.Caster is a plain BaseUnit when a doodad or an item applied the buff; the old handler
        // dereferenced the caster's events here.
        var doodad = new BaseUnit { ObjId = 5 };
        var applier = Unit(2);
        Exception thrown = null;
        try
        {
            AttachDebuff(doodad, applier,
                new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Damaged, Effect = new RecordingEffect() });
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNull();
    }

    [Test]
    public async Task Unsubscribe_StopsEverySubscription()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var effect = new RecordingEffect();
        var buff = AttachDebuff(victim, applier,
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Damaged, Effect = effect });

        buff.Triggers.UnsubscribeEvents();

        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 5 });
        applier.Events.OnDamaged(applier, new OnDamagedArgs { Attacker = victim, Amount = 5 });

        await Assert.That(effect.Applications).IsEmpty();
    }

    [Test]
    public async Task OwnerGate_BlocksTheTriggerWhileTheOwnerCarriesTheTag()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var effect = new RecordingEffect();
        AttachDebuff(victim, applier, new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.Damaged,
            Effect = effect,
            OwnerNoBuffTagId = OwnerTagId
        });

        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 5 });
        await Assert.That(effect.Applications).Count().IsEqualTo(1);

        AddTaggedBuff(victim);
        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 5 });
        await Assert.That(effect.Applications).Count().IsEqualTo(1);
    }

    #endregion

    #region Timeout against dispel

    [Test]
    public async Task NaturalTimeout_RunsTheTimeoutTriggersAndNotTheDispelledOnes()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var timeoutEffect = new RecordingEffect();
        var dispelledEffect = new RecordingEffect();
        var buff = AttachDebuff(victim, applier,
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Timeout, Effect = timeoutEffect },
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Dispelled, Effect = dispelledEffect });

        buff.TimeOut();

        await Assert.That(timeoutEffect.Applications).Count().IsEqualTo(1);
        await Assert.That(dispelledEffect.Applications).IsEmpty();
    }

    [Test]
    public async Task ExplicitRemoval_RunsTheDispelledTriggersAndNotTheTimeoutOnes()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var timeoutEffect = new RecordingEffect();
        var dispelledEffect = new RecordingEffect();
        AttachDebuff(victim, applier,
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Timeout, Effect = timeoutEffect },
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Dispelled, Effect = dispelledEffect });

        victim.Buffs.RemoveBuff(DebuffId, notifyZone: false);

        await Assert.That(dispelledEffect.Applications).Count().IsEqualTo(1);
        await Assert.That(timeoutEffect.Applications).IsEmpty();
        await Assert.That(victim.Buffs.CheckBuff(DebuffId)).IsFalse();
    }

    [Test]
    public async Task EndedBuff_IsUnsubscribedFromItsOwner()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var timeoutEffect = new RecordingEffect();
        var buff = AttachDebuff(victim, applier,
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Timeout, Effect = timeoutEffect });

        buff.TimeOut();
        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 5 });

        // The timeout ran once and the handler let go of the buff: a second timeout cannot fire.
        await Assert.That(timeoutEffect.Applications).Count().IsEqualTo(1);
        buff.TimeOut();
        await Assert.That(timeoutEffect.Applications).Count().IsEqualTo(1);
    }

    #endregion

    #region Delay

    [Test]
    public async Task DelayTime_QueuesTheEffectInsteadOfApplyingItInline()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var effect = new RecordingEffect();
        AttachDebuff(victim, applier, new BuffTriggerTemplate
        {
            Kind = BuffEventTriggerKind.Damaged,
            Effect = effect,
            UseDamageAmount = true,
            DelayTime = 2500
        });

        var before = DateTime.UtcNow;
        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 33 });
        await Assert.That(effect.Applications).IsEmpty();

        var queued = QueuedTasks().OfType<BuffTriggerTask>().ToList();
        await Assert.That(queued).HasSingleItem();
        await Assert.That(queued[0].TriggerTime - before >= TimeSpan.FromMilliseconds(2000)).IsTrue();

        queued[0].Execute();

        await Assert.That(effect.Applications).Count().IsEqualTo(1);
        await Assert.That(effect.Applications[0].Target).IsSameReferenceAs(victim);
        await Assert.That(effect.Applications[0].Amount).IsEqualTo(33);
    }

    [Test]
    public async Task ZeroDelay_AppliesInlineAndQueuesNothing()
    {
        var victim = Unit(1);
        var applier = Unit(2);
        var effect = new RecordingEffect();
        AttachDebuff(victim, applier,
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Damaged, Effect = effect });

        victim.Events.OnDamaged(victim, new OnDamagedArgs { Attacker = applier, Amount = 33 });

        await Assert.That(effect.Applications).Count().IsEqualTo(1);
        await Assert.That(QueuedTasks().OfType<BuffTriggerTask>()).IsEmpty();
    }

    #endregion

    /// <summary>
    /// The buff the regression is about: applied by <paramref name="applier"/> on
    /// <paramref name="owner"/>, carrying the given trigger rows.
    /// </summary>
    private static Buff AttachDebuff(BaseUnit owner, Unit applier, params BuffTriggerTemplate[] rows)
    {
        SetField(SkillManager.Instance, "_buffTriggers",
            new Dictionary<uint, List<BuffTriggerTemplate>> { [DebuffId] = [.. rows] });

        var buff = new Buff(owner, applier, new SkillCasterUnit(applier.ObjId),
            SkillManager.Instance.GetBuffTemplate(DebuffId), null, DateTime.UtcNow)
        {
            Passive = true, // keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test
            AbLevel = 1
        };

        owner.Buffs.AddBuff(buff);
        return buff;
    }

    private static void AddTaggedBuff(Unit unit) =>
        unit.Buffs.AddBuff(new Buff(unit, unit, new SkillCasterUnit(unit.ObjId),
            SkillManager.Instance.GetBuffTemplate(TaggedBuffId), null, DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        });

    private static List<GameTask> QueuedTasks()
    {
        var queueField = typeof(TaskManager).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = (ConcurrentDictionary<uint, GameTask>)queueField.GetValue(TaskManager.Instance)!;
        return [.. queue.Values];
    }

    private static Unit Unit(uint objId) => new() { ObjId = objId };

    private static SkillManager CreateSkillManager()
    {
        var manager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(manager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [DebuffId] = new BuffTemplate { Id = DebuffId, Duration = 0 },
            [TaggedBuffId] = new BuffTemplate { Id = TaggedBuffId, Duration = 0 }
        });
        SetField(manager, "_taggedBuffs", new Dictionary<uint, List<uint>> { [OwnerTagId] = [TaggedBuffId] });
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
}
