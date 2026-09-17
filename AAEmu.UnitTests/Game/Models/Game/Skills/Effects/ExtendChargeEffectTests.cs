using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

using TUnit.Mocks;

using TaskManager = AAEmu.Game.Core.Managers.TaskManager;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="ExtendChargeEffect"/> against a real <see cref="Buffs"/> container and a real
/// <see cref="Buff"/>: skill 10153 보호막's shield buff goes up by the charge the row composes.
/// </summary>
/// <remarks>
/// The world is not on this path. <see cref="Buffs.AddBuff(Buff, uint, int)"/> broadcasts to
/// <c>WorldManager.GetAround</c>, which is empty for an unparented unit, and the two content lookups it
/// makes (<c>SkillManager</c> and <c>BuffGameData</c>) are the empty ones the test helpers install.
/// </remarks>
[NotInParallel]
public class ExtendChargeEffectTests
{
    // Skill 10153 보호막 applies extend-charge effect 68337 (actual id 1) and grants buff 95 보호막,
    // whose own text reads "보호량: … + 자신의 최대 활력 5%" — the 5 % the row authors.
    private const uint ShieldBuffId = 95;
    private const int ShieldMaxCharge = 0; // no ceiling authored

    private SingletonScope<SkillManager> _skills;
    private SingletonScope<BuffGameData> _buffGameData;
    private SingletonScope<TaskManager> _tasks;
    private SingletonScope<EffectTaskManager> _effectTasks;

    [Before(Test)]
    public void InstallContentLookups()
    {
        var taskManager = new TaskManager(Mock.Of<ITickManager>().Object);
        _skills = new SingletonScope<SkillManager>(BuildSkillManager());
        _buffGameData = new SingletonScope<BuffGameData>(BuildBuffGameData());
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

    [Test]
    public async Task CastingOnAUnitThatAlreadyHasTheShield_RaisesItsCharge()
    {
        var (caster, target) = CreatePair();
        var shield = ApplyShield(target, charge: 4000);

        Cast(caster, target);

        await Assert.That(shield.Charge).IsEqualTo(5500);
    }

    [Test]
    public async Task TheSameChargeIsAddedAgainOnASecondCast()
    {
        var (caster, target) = CreatePair();
        var shield = ApplyShield(target, charge: 4000);

        Cast(caster, target);
        Cast(caster, target);

        await Assert.That(shield.Charge).IsEqualTo(7000);
    }

    [Test]
    public async Task AnEffectThatEnablesNothing_LeavesTheChargeExactlyWhereItWas()
    {
        var (caster, target) = CreatePair();
        var shield = ApplyShield(target, charge: 4000);

        // Every source flag off: the pin that an unconfigured row is a no-op.
        ExtendCharge(caster, target, new ExtendChargeEffect
        {
            Id = 999,
            ChargeBuffId = (int)ShieldBuffId
        });

        await Assert.That(shield.Charge).IsEqualTo(4000);
    }

    [Test]
    public async Task AnEffectNamingNoBuffIsANoOp()
    {
        var (caster, target) = CreatePair();
        var shield = ApplyShield(target, charge: 4000);

        ExtendCharge(caster, target, new ExtendChargeEffect
        {
            Id = 999,
            ChargeBuffId = 0,
            UseFixedCharge = true,
            FixedMin = 5000,
            FixedMax = 5000
        });

        await Assert.That(shield.Charge).IsEqualTo(4000);
    }

    [Test]
    public async Task TheAddedChargeIsHeldAtTheBuffsOwnCeiling()
    {
        var (caster, target) = CreatePair();
        var shield = ApplyShield(target, charge: 19_500, maxCharge: 20_000);

        ExtendCharge(caster, target, new ExtendChargeEffect
        {
            Id = 13,
            ChargeBuffId = (int)ShieldBuffId,
            UseFixedCharge = true,
            FixedMin = 1500,
            FixedMax = 1500
        });

        await Assert.That(shield.Charge).IsEqualTo(20_000);
    }

    [Test]
    public async Task WithoutTheShieldUp_TheEffectGrantsItWithTheCharge()
    {
        // No skill_effects row anywhere applies these 14 shields through a BuffEffect, so this effect is the
        // only thing that can put one up. Skill 10153's own text is "기술 사용 시 보호막".
        var (caster, target) = CreatePair();

        ExtendCharge(caster, target, new ExtendChargeEffect
        {
            Id = 1,
            ChargeBuffId = (int)ShieldBuffId,
            UseFixedCharge = true,
            FixedMin = 1500,
            FixedMax = 1500
        });

        var shield = target.Buffs.GetEffectFromBuffId(ShieldBuffId);
        await Assert.That(shield).IsNotNull();
        await Assert.That(shield.Charge).IsEqualTo(1500);
    }

    [Test]
    public async Task TheShieldIsTheAbsorptionBuffReduceCurrentHpSpends()
    {
        // damage_absorption_type_id 2 on buff 95 is what Buffs.GetAbsorptionEffects selects.
        var (_, target) = CreatePair();
        ApplyShield(target, charge: 4000);

        await Assert.That(target.Buffs.GetAbsorptionEffects().Select(b => b.Template.BuffId))
            .Contains(ShieldBuffId);
    }

    private static (Unit Caster, Unit Target) CreatePair() =>
        (new Unit { ObjId = 300, Level = 50, Hp = 10_000, MaxHp = 10_000, Mp = 6_000, MaxMp = 30_000 },
         new Unit { ObjId = 301, Level = 50, Hp = 10_000, MaxHp = 10_000, Mp = 6_000, MaxMp = 30_000 });

    /// <summary>Puts the shipped shield buff on the unit the way the content does.</summary>
    private static Buff ApplyShield(Unit unit, int charge, int maxCharge = ShieldMaxCharge)
    {
        var template = new BuffTemplate
        {
            Id = ShieldBuffId,
            Duration = 40_000,
            StackRule = BuffStackRule.ChargeRefresh,
            MaxStack = 1,
            InitMinCharge = 1,
            InitMaxCharge = 15_000,
            MaxCharge = maxCharge,
            DamageAbsorptionTypeId = 2
        };

        var buff = new Buff(unit, unit, new SkillCasterUnit(unit.ObjId), template, null, DateTime.UtcNow)
        {
            Charge = charge
        };

        unit.Buffs.AddBuff(buff);
        return unit.Buffs.GetEffectFromBuffId(ShieldBuffId);
    }

    private static void Cast(Unit caster, Unit target)
    {
        // extend-charge effect 1: 5 % of max_mana (30,000 -> 1,500), level 1.05 x a 1,000 rating and
        // dps_inc_multiplier 1.5 x a melee dps_inc of 0.
        ExtendCharge(caster, target, new ExtendChargeEffect
        {
            Id = 1,
            ChargeBuffId = (int)ShieldBuffId,
            DamageTypeId = 2,
            UsePercentCharge = true,
            PercentMin = 5,
            PercentMax = 5,
            PercentDamageResourceTypeId = 4,
            UseLevelCharge = true,
            LevelMd = 1.05f,
            LevelVaStart = 1,
            LevelVaEnd = 1,
            UseDpsCharge = true,
            DpsIncMultiplier = 1.5f,
            DpsMultiplier = 1f
        });
    }

    private static void ExtendCharge(Unit caster, Unit target, ExtendChargeEffect effect)
    {
        effect.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            new CastSkill(10153, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);
    }

    private static SkillManager BuildSkillManager()
    {
        var manager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(manager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [ShieldBuffId] = new BuffTemplate
            {
                Id = ShieldBuffId,
                Duration = 40_000,
                StackRule = BuffStackRule.ChargeRefresh,
                MaxStack = 1,
                InitMinCharge = 1,
                InitMaxCharge = 15_000,
                DamageAbsorptionTypeId = 2
            }
        });
        SetField(manager, "_taggedBuffs", new Dictionary<uint, List<uint>>());
        return manager;
    }

    private static BuffGameData BuildBuffGameData()
    {
        var gameData = new BuffGameData();
        SetField(gameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(gameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(gameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        return gameData;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
