using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <c>heal_effects.percent</c> (237 rows), <c>self_target_multiplier</c> (152 rows at 0.7) and
/// <c>ignore_heal_aggro</c> (28 rows) through the real <see cref="HealEffect"/> path.
/// </summary>
/// <remarks>
/// The healer's heal dps rating is the only term of the absolute composition (no cast time, no charged
/// buff), so an absolute heal is deterministic: rating 100000 gives 100. The percent branch does not read
/// the caster at all.
/// </remarks>
[NotInParallel]
public class HealEffectPercentTests
{
    private const int HealDpsRating = 100_000; // rating * 0.001f * DpsMultiplier = 100
    private const int TargetMaxHp = 1_000;
    private const int TargetStartHp = 1;
    private const uint HealerObjId = 300;
    private const uint TargetObjId = 301;

    [Test]
    public async Task Percent_TenPercentHealsTenPercent()
    {
        var healer = CreateHealer();
        var target = CreateTarget();

        Heal(healer, target, new HealEffect { Id = 1, Percent = true, FixedMin = 10, FixedMax = 10 });

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(100);
    }

    [Test]
    public async Task Percent_IgnoresTheAbsoluteComposition()
    {
        // The same row read as an absolute heal would pay 10 (fixed) or 100 (heal dps). It pays the share.
        var healer = CreateHealer();
        var target = CreateTarget();

        Heal(healer, target, new HealEffect
        {
            Id = 1,
            Percent = true,
            FixedMin = 10,
            FixedMax = 10,
            UseFixedHeal = true,
            DpsMultiplier = 1f
        });

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(100);
    }

    [Test]
    public async Task Percent_RangeRollsInsideTheRange()
    {
        var healer = CreateHealer();
        var target = CreateTarget();

        // heal effect 813 authors 10-50.
        for (var i = 0; i < 200; i++)
        {
            target.Hp = TargetStartHp;
            Heal(healer, target, new HealEffect { Id = 813, Percent = true, FixedMin = 10, FixedMax = 50 });

            var healed = target.Hp - TargetStartHp;
            await Assert.That(healed).IsGreaterThanOrEqualTo(100);
            await Assert.That(healed).IsLessThanOrEqualTo(500);
        }
    }

    [Test]
    public async Task Percent_UsesTheTargetsOwnCeiling()
    {
        var healer = CreateHealer();
        var target = CreateTarget();
        target.MaxHp = 2_000;

        Heal(healer, target, new HealEffect { Id = 1, Percent = true, FixedMin = 10, FixedMax = 10 });

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(200);
    }

    [Test]
    public async Task Percent_HealsEvenWhenTheCasterHasNoHealRating()
    {
        // The share is the healed unit's, so an unarmed or low-level caster heals the same percentage.
        var healer = CreateHealer();
        healer.HDps = 0;
        var target = CreateTarget();

        Heal(healer, target, new HealEffect { Id = 1, Percent = true, FixedMin = 10, FixedMax = 10 });

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(100);
    }

    [Test]
    public async Task WithoutPercent_TheHealIsExactlyWhatItWas()
    {
        // The byte-identical pin: no percent column set means the absolute composition, unchanged.
        var healer = CreateHealer();
        var target = CreateTarget();

        Heal(healer, target, new HealEffect { Id = 1, DpsMultiplier = 1f });

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(100);
    }

    [Test]
    public async Task SelfTargetMultiplier_ScalesASelfHeal()
    {
        var healer = CreateHealer();
        healer.Hp = TargetStartHp;
        healer.MaxHp = TargetMaxHp;

        Heal(healer, healer, new HealEffect
        {
            Id = 1,
            DpsMultiplier = 1f,
            SelfTargetMul = 0.7f
        });

        await Assert.That(healer.Hp - TargetStartHp).IsEqualTo(70);
    }

    [Test]
    public async Task SelfTargetMultiplier_DoesNotTouchAHealOnSomebodyElse()
    {
        var healer = CreateHealer();
        var target = CreateTarget();

        Heal(healer, target, new HealEffect
        {
            Id = 1,
            DpsMultiplier = 1f,
            SelfTargetMul = 0.7f
        });

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(100);
    }

    [Test]
    public async Task SelfTargetMultiplier_AtOneIsNeutral()
    {
        var healer = CreateHealer();
        healer.Hp = TargetStartHp;
        healer.MaxHp = TargetMaxHp;

        Heal(healer, healer, new HealEffect
        {
            Id = 1,
            DpsMultiplier = 1f,
            SelfTargetMul = 1.0f
        });

        await Assert.That(healer.Hp - TargetStartHp).IsEqualTo(100);
    }

    [Test]
    public async Task SelfTargetMultiplier_MissingRowReadsAsNeutral()
    {
        // 0 is "no such column", not "heal for nothing".
        var healer = CreateHealer();
        healer.Hp = TargetStartHp;
        healer.MaxHp = TargetMaxHp;

        Heal(healer, healer, new HealEffect { Id = 1, DpsMultiplier = 1f });

        await Assert.That(healer.Hp - TargetStartHp).IsEqualTo(100);
    }

    [Test]
    public async Task SelfTargetMultiplier_ScalesAPercentSelfHeal()
    {
        var healer = CreateHealer();
        healer.Hp = TargetStartHp;
        healer.MaxHp = TargetMaxHp;

        Heal(healer, healer, new HealEffect
        {
            Id = 1,
            Percent = true,
            FixedMin = 10,
            FixedMax = 10,
            SelfTargetMul = 0.7f
        });

        await Assert.That(healer.Hp - TargetStartHp).IsEqualTo(70);
    }

    [Test]
    public async Task UnspawnedUnitsAreNotTheSameUnit()
    {
        // Two zero-obj-id units are not a self-cast; the multiplier stays neutral.
        await Assert.That(HealEffectRules.IsSelfTarget(0, 0)).IsFalse();
        await Assert.That(HealEffectRules.IsSelfTarget(HealerObjId, 0)).IsFalse();
        await Assert.That(HealEffectRules.IsSelfTarget(HealerObjId, HealerObjId)).IsTrue();
    }

    private static Unit CreateHealer()
    {
        // A distinct obj id from the target: the self-target multiplier must not fire on a plain heal.
        // MaxHp is well above any heal these tests compose so the clamp never hides a number.
        return new Unit { ObjId = HealerObjId, Level = 50, Hp = TargetStartHp, MaxHp = 100_000, HDps = HealDpsRating };
    }

    private static Unit CreateTarget() => new() { ObjId = TargetObjId, Level = 50, Hp = TargetStartHp, MaxHp = TargetMaxHp };

    private static void Heal(Unit healer, Unit target, HealEffect effect)
    {
        effect.Apply(
            healer,
            new SkillCasterUnit(healer.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);
    }
}
