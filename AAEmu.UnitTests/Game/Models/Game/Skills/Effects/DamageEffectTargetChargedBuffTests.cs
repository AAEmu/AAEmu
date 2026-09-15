using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The <c>use_target_charged_buff</c> branch has to count the charges of the buff the <b>target</b>
/// carries and price them at <c>target_charged_mul</c>. It read the caster's <c>ChargedBuffId</c> /
/// <c>ChargedMul</c> instead, so the add was measured against a buff id no unit carried, or paid at
/// the wrong price.
/// </summary>
/// <remarks>
/// content: damage_effects 3602 (charged_buff_id NULL with charged_mul 1.0; target_charged_buff_id
/// 3930 검은 비늘 군단의 낙인, max_charge 30, target_charged_mul 60) is referenced by skill_effect
/// 17678 on enabled skill 18825 고드프리의 참수 — the branch added nothing at all there. 4249 and 5340
/// (buff 899 누적 피해, max_charge 1000, on both sides, charged_mul 50 against target_charged_mul 100)
/// paid half.
/// </remarks>
[NotInParallel]
public class DamageEffectTargetChargedBuffTests
{
    private const uint SkillId = 18825;            // 고드프리의 참수
    private const uint CasterChargedBuffId = 899;  // 누적 피해, max_charge 1000
    private const uint TargetChargedBuffId = 3930; // 검은 비늘 군단의 낙인, max_charge 30

    private FieldInfo _skillManagerField;
    private object _previousSkillManager;

    [Before(Test)]
    public void InstallSkillManager()
    {
        // DamageEffect asks SkillModifiers for the skill's damage modifiers, which reads the skill-tag
        // table. A manager with empty tables answers every lookup with an empty list.
        _skillManagerField = typeof(Singleton<SkillManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousSkillManager = _skillManagerField.GetValue(null);
        _skillManagerField.SetValue(null,
            new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object));
    }

    [After(Test)]
    public void RestoreSkillManager() => _skillManagerField.SetValue(null, _previousSkillManager);

    [Test]
    public async Task Apply_TargetChargedBuff_CountsTheChargesOnTheTargetsBuff()
    {
        // damage_effects 3602's shape: the caster pair is empty (NULL id, multiplier 1.0) and only the
        // target pair names a buff, so the caster-side read found buff id 0 and added nothing.
        var caster = CreateCaster();
        var target = CreateTarget();
        var targetBuffs = Mock.Of<IBuffs>();
        targetBuffs.GetEffectFromBuffId(TargetChargedBuffId)
            .Returns(CreateChargedBuff(target, caster, TargetChargedBuffId, charges: 30));
        // The id the broken branch asked for: no unit carries buff 0.
        targetBuffs.GetEffectFromBuffId(0u).Returns((Buff)null);
        targetBuffs.GetAbsorptionEffects().Returns([]);
        target.Buffs = targetBuffs.Object;

        Apply(CreateEffect(chargedBuffId: 0, chargedMul: 1f, TargetChargedBuffId, targetChargedMul: 60f), caster, target);

        // 30 charges * target_charged_mul 60 = 1800, on a base hit that cannot exceed ~200.
        var damage = target.MaxHp - target.Hp;
        await Assert.That(damage).IsGreaterThanOrEqualTo(1800);
        await Assert.That(damage).IsLessThanOrEqualTo(2100);
    }

    [Test]
    public async Task Apply_TargetChargedBuff_PaysTheTargetsMultiplier()
    {
        // damage_effects 4249 / 5340's shape: buff 899 sits on both sides, so only the price separates
        // the two branches — charged_mul 50 against target_charged_mul 100 over the target's 1000 charges.
        var caster = CreateCaster();
        var target = CreateTarget();
        var targetBuffs = Mock.Of<IBuffs>();
        targetBuffs.GetEffectFromBuffId(CasterChargedBuffId)
            .Returns(CreateChargedBuff(target, caster, CasterChargedBuffId, charges: 1000));
        targetBuffs.GetAbsorptionEffects().Returns([]);
        target.Buffs = targetBuffs.Object;

        Apply(CreateEffect(CasterChargedBuffId, chargedMul: 50f, CasterChargedBuffId, targetChargedMul: 100f), caster, target);

        var damage = target.MaxHp - target.Hp;
        await Assert.That(damage).IsGreaterThanOrEqualTo(100_000);
        await Assert.That(damage).IsLessThanOrEqualTo(100_500);
    }

    [Test]
    public async Task Apply_TargetChargedBuff_WithoutASkill_StillPaysTheTargetsCharges()
    {
        // damage_effects 4249, 5340 and 7140 are reached through buff_triggers rather than skill_effects,
        // and a trigger's EffectSource carries no Skill, so the branch used to be skipped entirely there.
        var caster = CreateCaster();
        var target = CreateTarget();
        var targetBuffs = Mock.Of<IBuffs>();
        targetBuffs.GetEffectFromBuffId(CasterChargedBuffId)
            .Returns(CreateChargedBuff(target, caster, CasterChargedBuffId, charges: 1000));
        targetBuffs.GetAbsorptionEffects().Returns([]);
        target.Buffs = targetBuffs.Object;

        ApplyAsTrigger(CreateEffect(CasterChargedBuffId, chargedMul: 50f, CasterChargedBuffId, targetChargedMul: 100f),
            caster, target);

        var damage = target.MaxHp - target.Hp;
        await Assert.That(damage).IsGreaterThanOrEqualTo(100_000);
        await Assert.That(damage).IsLessThanOrEqualTo(100_500);
    }

    private static Unit CreateCaster() => new()
    {
        ObjId = 500,
        Name = "Caster",
        Level = 50,
        LevelDps = 100f,
        Hp = 1_000_000,
        MaxHp = 1_000_000
    };

    private static Unit CreateTarget() => new()
    {
        ObjId = 600,
        Name = "Target",
        Level = 50,
        Hp = 1_000_000,
        MaxHp = 1_000_000,
        IncomingDamageMul = 1f
    };

    private static Buff CreateChargedBuff(BaseUnit owner, BaseUnit caster, uint buffId, int charges) =>
        new(owner, caster, new SkillCasterUnit(caster.ObjId),
            new BuffTemplate { Id = buffId }, null, DateTime.UtcNow)
        {
            Charge = charges
        };

    /// <summary>
    /// A row shaped like damage_effects 3602: level damage only, no weapon, no fixed damage, so the
    /// base hit stays small and the charge add is what the assertion measures.
    /// </summary>
    private static DamageEffect CreateEffect(uint chargedBuffId, float chargedMul, uint targetChargedBuffId, float targetChargedMul) => new()
    {
        Id = 3602,
        DamageType = DamageType.Siege, // flat damage multiplier, no critical roll
        Multiplier = 1f,
        UseLevelDamage = true,
        LevelMd = 1f,
        LevelVaStart = 100,
        LevelVaEnd = 100,
        WeaponSlotId = -1,
        UseChargedBuff = false,
        ChargedBuffId = chargedBuffId,
        ChargedMul = chargedMul,
        ChargedLevelMul = 0f,
        UseTargetChargedBuff = true,
        TargetChargedBuffId = targetChargedBuffId,
        TargetChargedMul = targetChargedMul
    };

    private static void Apply(DamageEffect effect, Unit caster, Unit target)
    {
        var skill = new Skill
        {
            Template = new SkillTemplate { Id = SkillId, CastingTime = 1000 },
            Level = 1
        };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastSkill(SkillId, 1), new EffectSource(skill), null, DateTime.UtcNow);
    }

    /// <summary>
    /// The shape a buff trigger uses: a <see cref="CastBuff"/> action and an <see cref="EffectSource"/>
    /// carrying the buff with no skill behind it, which is what a trigger with no skill sends.
    /// </summary>
    private static void ApplyAsTrigger(DamageEffect effect, Unit caster, Unit target)
    {
        var buff = CreateChargedBuff(target, caster, CasterChargedBuffId, charges: 0);

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), target, new SkillCastUnitTarget(target.ObjId),
            new CastBuff(buff), new EffectSource(buff.Template), null, DateTime.UtcNow);
    }
}
