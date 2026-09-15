using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <c>heal_damage_mul</c> (<c>unit_modifiers</c> 222) through the real <see cref="HealEffect"/> path: the
/// caster's own heal output, applied to the composed heal, and deliberately not a damage multiplier.
/// </summary>
/// <remarks>
/// Like the damage side, the healer is a plain <see cref="Unit"/> whose heal dps rating is the only term
/// of the composition (no cast time, no charged buff), so the heal is deterministic: rating 100000 gives
/// 100 before any multiplier.
/// </remarks>
[NotInParallel]
public class HealDamageMultiplierTests
{
    private const int ComposedHeal = 100;
    private const int HealDpsRating = 100_000; // rating * 0.001f * DpsMultiplier = 100
    private const int TargetStartHp = 1_000;
    private const int TargetMaxHp = 100_000;

    [Test]
    public async Task HealDamageMul_ScalesTheHeal()
    {
        var healer = CreateHealer((UnitAttribute.HealDamageMul, 1000L));
        var target = CreateTarget();

        Heal(healer, target);

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(ComposedHeal * 2);
    }

    [Test]
    public async Task HealDamageMul_UsesTheShippedPerMilleScale()
    {
        // 2500 is what 광폭화/폭주 carry, 1000 what the flat "+100% healing" rows carry.
        var healer = CreateHealer((UnitAttribute.HealDamageMul, 2500L));
        var target = CreateTarget();

        Heal(healer, target);

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo((int)(ComposedHeal * 3.5f));
    }

    [Test]
    public async Task WithoutTheRow_TheHealIsUnchanged()
    {
        // The byte-identical pin for the heal side.
        var healer = CreateHealer();
        var target = CreateTarget();

        Heal(healer, target);

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(ComposedHeal);
    }

    [Test]
    public async Task AVictimsOwnHealDamageMul_IsNotUsed()
    {
        // It is the healer's attribute, not the healed unit's; the target's own copy must not move it.
        var healer = CreateHealer();
        var target = CreateTarget();
        TestBuffModifier.Apply(target, UnitAttribute.HealDamageMul, 1000);

        Heal(healer, target);

        await Assert.That(target.Hp - TargetStartHp).IsEqualTo(ComposedHeal);
    }

    [Test]
    public async Task HealDamageMul_DoesNotTouchDamage()
    {
        var healer = CreateHealer((UnitAttribute.HealDamageMul, 2500L));
        healer.DpsInc = HealDpsRating; // so a melee effect has its own source of damage
        var victim = new Unit { ObjId = 400, Hp = 20_000 };

        var effect = new DamageEffect
        {
            Id = 2,
            DamageType = DamageType.Melee,
            Multiplier = 1f,
            DpsIncMultiplier = 1f,
            WeaponSlotId = -1
        };

        effect.Apply(
            healer,
            new SkillCasterUnit(healer.ObjId),
            victim,
            new SkillCastUnitTarget(victim.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);

        await Assert.That(20_000 - victim.Hp).IsEqualTo(ComposedHeal);
    }

    private static Unit CreateHealer(params (UnitAttribute Attribute, long Value)[] rows)
    {
        var healer = new Unit { ObjId = 300, Level = 50, Hp = TargetStartHp, MaxHp = TargetMaxHp, HDps = HealDpsRating };

        for (var i = 0; i < rows.Length; i++)
            TestBuffModifier.Apply(healer, rows[i].Attribute, rows[i].Value, buffIndex: (uint)(i + 1));

        return healer;
    }

    private static Unit CreateTarget() => new() { ObjId = 301, Level = 50, Hp = TargetStartHp, MaxHp = TargetMaxHp };

    private static void Heal(Unit healer, Unit target)
    {
        var effect = new HealEffect
        {
            Id = 1,
            DpsMultiplier = 1f
        };

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
