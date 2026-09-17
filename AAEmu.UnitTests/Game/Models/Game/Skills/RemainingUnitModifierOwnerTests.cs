using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The three <c>unit_modifiers</c> owner types this batch picked up: <c>Buffs</c> (232 rows, the buff's own
/// bonuses under the other spelling of the same owner), <c>BuffUnitModifier</c> together with
/// <c>buff_unit_modifiers</c> (160 + 160 rows, a modifier that only reaches units carrying a tag), and
/// <c>HealEffect</c> (160 rows, 158 of them <c>heal_critical_mul</c> -2000).
/// </summary>
public class RemainingUnitModifierOwnerTests
{
    private static Buff LiveBuff(BuffTemplate template) => new(
        new Unit(), new Unit(), new SkillCasterUnit(1), template, null, DateTime.UtcNow)
    {
        Index = 10u,
        Passive = true,
        AbLevel = 1
    };

    private static BonusTemplate MoveSpeed(long value) => new()
    {
        Attribute = UnitAttribute.MoveSpeedMul,
        ModifierType = UnitModifierType.Value,
        Value = value
    };

    [Test]
    public async Task BuffsOwnerRows_LowerMoveSpeed()
    {
        // unit_modifiers 375991: owner_type='Buffs', owner_id 27386, attribute 10 move_speed_mul, value -30.
        // The row reaches the buff's Bonuses like an owner_type='Buff' row does, so the buff slows its owner
        // by 3 per-cent; before this batch every one of the 232 'Buffs' rows was dropped by the loader.
        var owner = new Unit { ObjId = 1 };
        var caster = new Unit { ObjId = 2 };
        var template = new BuffTemplate { Id = 27386 };
        template.Bonuses.Add(MoveSpeed(-30));

        template.Start(caster, owner, LiveBuff(template));

        await Assert.That(owner.MoveSpeedMul).IsEqualTo(0.97f);
    }

    [Test]
    public async Task WithoutTheRow_MoveSpeedIsExactlyOne()
    {
        var owner = new Unit { ObjId = 1 };
        var caster = new Unit { ObjId = 2 };
        var template = new BuffTemplate { Id = 27386 };

        template.Start(caster, owner, LiveBuff(template));

        await Assert.That(owner.MoveSpeedMul).IsEqualTo(1f);
    }

    [Test]
    public async Task BuffUnitModifierRows_ReachOnlyTheUnitsCarryingTheNamedBuff()
    {
        // buff_unit_modifiers 40 is owned by buff 15793 and names buff 2675 (Dash); its unit_modifiers row is
        // attribute 10 move_speed_mul +300, i.e. 30% faster while dashing.
        var template = new BuffTemplate { Id = 15793 };
        var selector = new BuffUnitModifierTemplate { Id = 40, BuffId = 2675 };
        selector.Bonuses.Add(MoveSpeed(300));
        template.UnitModifierSelectors.Add(selector);

        var dashing = new Unit { ObjId = 1 };
        var dashBuffs = Mock.Of<IBuffs>();
        dashBuffs.CheckBuff(2675u).Returns(true);
        dashing.Buffs = dashBuffs.Object;

        var walking = new Unit { ObjId = 2 };

        template.Start(new Unit { ObjId = 3 }, dashing, LiveBuff(template));
        template.Start(new Unit { ObjId = 3 }, walking, LiveBuff(template));

        await Assert.That(dashing.MoveSpeedMul).IsEqualTo(1.3f);
        await Assert.That(walking.MoveSpeedMul).IsEqualTo(1f);
    }

    [Test]
    public async Task BuffUnitModifierSelectors_AreMatchedByTagToo()
    {
        var template = new BuffTemplate { Id = 1 };
        var selector = new BuffUnitModifierTemplate { Id = 9, TagId = 55 };
        selector.Bonuses.Add(MoveSpeed(300));
        template.UnitModifierSelectors.Add(selector);

        var tagged = new Unit { ObjId = 1 };
        var buffs = Mock.Of<IBuffs>();
        buffs.CheckBuffTag(55u).Returns(true);
        tagged.Buffs = buffs.Object;

        var untagged = new Unit { ObjId = 2 };

        template.Start(new Unit { ObjId = 3 }, tagged, LiveBuff(template));
        template.Start(new Unit { ObjId = 3 }, untagged, LiveBuff(template));

        await Assert.That(tagged.MoveSpeedMul).IsEqualTo(1.3f);
        await Assert.That(untagged.MoveSpeedMul).IsEqualTo(1f);
    }

    [Test]
    public async Task HealCriticalMul_OfMinus2000_IsNeverACritical()
    {
        // unit_modifiers 60731: owner_type='HealEffect', owner_id 1200, attribute 185 heal_critical_mul,
        // value -2000. 1000 + (-2000) over 1000 is -1, and a negative multiplier cannot crit.
        var bonuses = new List<BonusTemplate>
        {
            new() { Attribute = UnitAttribute.HealCriticalMul, ModifierType = UnitModifierType.Value, Value = -2000 }
        };

        var multiplier = HealEffectRules.CriticalMultiplier(bonuses);

        await Assert.That(multiplier).IsEqualTo(-1d);
        await Assert.That(HealEffectRules.CanCrit(multiplier)).IsFalse();
    }

    [Test]
    public async Task WithoutTheRow_TheCriticalMultiplierIsExactlyOne()
    {
        await Assert.That(HealEffectRules.CriticalMultiplier([])).IsEqualTo(1d);
        await Assert.That(HealEffectRules.CriticalMultiplier(null)).IsEqualTo(1d);
        await Assert.That(HealEffectRules.CanCrit(1d)).IsTrue();
    }

    [Test]
    public async Task AHealEffectCarryingTheRow_NeverLandsACritical()
    {
        // The healer's critical chance is 100, so without the row every roll is a critical (150 instead of
        // 100); with the effect's own -2000 row every roll is the plain 100.
        var caster = new Unit { ObjId = 300, Level = 50, Hp = 1_000, MaxHp = 100_000, HDps = 100_000, HealCritical = 100f, HealCriticalBonus = 50f };

        var plain = new Unit { ObjId = 301, Level = 50, Hp = 1_000, MaxHp = 100_000 };
        Heal(caster, plain, []);

        var capped = new Unit { ObjId = 302, Level = 50, Hp = 1_000, MaxHp = 100_000 };
        Heal(caster, capped,
        [
            new BonusTemplate { Attribute = UnitAttribute.HealCriticalMul, ModifierType = UnitModifierType.Value, Value = -2000 }
        ]);

        await Assert.That(plain.Hp - 1_000).IsEqualTo(150);
        await Assert.That(capped.Hp - 1_000).IsEqualTo(100);
    }

    private static void Heal(Unit caster, Unit target, List<BonusTemplate> bonuses)
    {
        var effect = new HealEffect { Id = 1, DpsMultiplier = 1f, Bonuses = bonuses };

        effect.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);
    }
}
