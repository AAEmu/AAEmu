using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The two remaining per-buff immunity flags that had no consumer: <c>knockback_immune</c> (913 buffs)
/// on the impulse path and <c>mana_burn_immune</c> (338 buffs) on the mana-burn path. Both are read off
/// the target's active buffs, the way <c>CheckDamageImmune</c> reads its own flags.
/// </summary>
[NotInParallel]
public class ImmunityEffectTests
{
    // Synthetic ids: the templates below are built here, only the two column names come from the DB.
    private const uint KnockbackImmuneBuffId = 91020;
    private const uint ManaBurnImmuneBuffId = 91021;
    private const uint PlainBuffId = 91022;
    private const uint TargetObjId = 11u;
    private const uint CasterObjId = 12u;

    private FieldInfo _skillManagerField;
    private FieldInfo _buffGameDataField;
    private FieldInfo _effectTaskManagerField;
    private FieldInfo _taskManagerField;
    private object _previousSkillManager;
    private object _previousBuffGameData;
    private object _previousEffectTaskManager;
    private object _previousTaskManager;
    private Action<uint, SkillCaster, float[], float[], float[], float[]> _previousImpulseRelay;

    [Before(Test)]
    public void InstallContentLookups()
    {
        _skillManagerField = SingletonField<SkillManager>();
        _buffGameDataField = SingletonField<BuffGameData>();
        _effectTaskManagerField = SingletonField<EffectTaskManager>();
        _taskManagerField = SingletonField<TaskManager>();
        _previousSkillManager = _skillManagerField.GetValue(null);
        _previousBuffGameData = _buffGameDataField.GetValue(null);
        _previousEffectTaskManager = _effectTaskManagerField.GetValue(null);
        _previousTaskManager = _taskManagerField.GetValue(null);
        _previousImpulseRelay = WorldIntegration.RelayImpulseToZone;

        var skillManager = new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object);
        SetField(skillManager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [KnockbackImmuneBuffId] = new() { Id = KnockbackImmuneBuffId, Duration = 10000, KnockbackImmune = true },
            [ManaBurnImmuneBuffId] = new() { Id = ManaBurnImmuneBuffId, Duration = 10000, ManaBurnImmune = true },
            [PlainBuffId] = new() { Id = PlainBuffId, Duration = 10000 }
        });
        _skillManagerField.SetValue(null, skillManager);

        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffGameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(buffGameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        _buffGameDataField.SetValue(null, buffGameData);

        var taskManager = new TaskManager(Mock.Of<ITickManager>().Object);
        _taskManagerField.SetValue(null, taskManager);
        _effectTaskManagerField.SetValue(null, new EffectTaskManager(taskManager));
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _skillManagerField.SetValue(null, _previousSkillManager);
        _buffGameDataField.SetValue(null, _previousBuffGameData);
        _effectTaskManagerField.SetValue(null, _previousEffectTaskManager);
        _taskManagerField.SetValue(null, _previousTaskManager);
        WorldIntegration.RelayImpulseToZone = _previousImpulseRelay;
    }

    #region knockback_immune

    [Test]
    public async Task Impulse_WhileKnockbackImmune_IsNotDelivered()
    {
        var (target, caster) = CreateUnits();
        var impulses = 0;
        WorldIntegration.RelayImpulseToZone = (_, _, _, _, _, _) => impulses++;
        AddBuff(target, caster, KnockbackImmuneBuffId);

        ApplyImpulse(target, caster);

        await Assert.That(impulses).IsEqualTo(0);
    }

    [Test]
    public async Task Impulse_WithoutKnockbackImmune_IsDelivered()
    {
        var (target, caster) = CreateUnits();
        var impulses = 0;
        WorldIntegration.RelayImpulseToZone = (_, _, _, _, _, _) => impulses++;
        AddBuff(target, caster, PlainBuffId);

        ApplyImpulse(target, caster);

        await Assert.That(impulses).IsEqualTo(1);
    }

    [Test]
    public async Task Impulse_WithoutAnyBuffs_IsDelivered()
    {
        var (target, caster) = CreateUnits();
        var impulses = 0;
        WorldIntegration.RelayImpulseToZone = (_, _, _, _, _, _) => impulses++;

        ApplyImpulse(target, caster);

        await Assert.That(impulses).IsEqualTo(1);
    }

    #endregion

    #region mana_burn_immune

    [Test]
    public async Task ManaBurn_WhileManaBurnImmune_LeavesManaAlone()
    {
        var (target, caster) = CreateUnits();
        AddBuff(target, caster, ManaBurnImmuneBuffId);
        var manaBefore = target.Mp;

        ApplyManaBurn(target, caster);

        await Assert.That(target.Mp).IsEqualTo(manaBefore);
    }

    [Test]
    public async Task ManaBurn_WithoutManaBurnImmune_DrainsMana()
    {
        var (target, caster) = CreateUnits();
        AddBuff(target, caster, PlainBuffId);
        var manaBefore = target.Mp;

        ApplyManaBurn(target, caster);

        await Assert.That(target.Mp).IsLessThan(manaBefore);
    }

    #endregion

    private static (Unit Target, Unit Caster) CreateUnits()
    {
        var target = new Unit { ObjId = TargetObjId, Hp = 1000, MaxHp = 1000, Mp = 5000, MaxMp = 5000 };
        var caster = new Unit { ObjId = CasterObjId, Hp = 1000, MaxHp = 1000, Mp = 5000, MaxMp = 5000 };
        return (target, caster);
    }

    private static void AddBuff(Unit owner, Unit caster, uint buffId)
    {
        owner.Buffs.AddBuff(new Buff(owner, caster, new SkillCasterUnit(caster.ObjId),
            SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow)
        {
            // Keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test with no connection.
            Passive = true,
            AbLevel = 1
        });
    }

    private static void ApplyImpulse(Unit target, Unit caster)
    {
        var effect = new ImpulseEffect { ImpulseZ = 250f };
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(0, 1), new EffectSource(), new SkillObject(), DateTime.UtcNow);
    }

    private static void ApplyManaBurn(Unit target, Unit caster)
    {
        var effect = new ManaBurnEffect { BaseMin = 100, BaseMax = 200 };
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(0, 1), new EffectSource(), new SkillObject(), DateTime.UtcNow);
    }

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
