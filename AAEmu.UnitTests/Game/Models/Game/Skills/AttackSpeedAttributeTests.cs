using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The attack-speed attributes through the real consumers: <c>melee_speed_mul</c>/<c>ranged_speed_mul</c>
/// (54/55) and <c>attack_speed_mul</c> (218) pace <see cref="SkillManager.GetAttackDelay"/>, and the
/// anti-miss multipliers (78/83/88) decide the combat dice in <see cref="Skill.RollCombatDice"/>.
/// </summary>
/// <remarks>
/// The intervals are the shipped fallbacks of <c>GetWeaponSpeed</c> (1500 ms melee, 1800 ms ranged) with no
/// equipment, and an empty <c>GlobalCooldownMul</c>, which is <c>100000 / (0 + 1000) / 100 = 1</c> for a
/// character. Every number below is therefore the attribute's own effect and nothing else. Comparisons that
/// pass through the rules class carry a 1 ms tolerance because the factor is a float, not because the two
/// sides are meant to differ.
/// </remarks>
public class AttackSpeedAttributeTests
{
    private const uint MeleeAutoAttack = 2;
    private const uint OffhandAutoAttack = 3;
    private const uint RangedAutoAttack = 4;
    private const uint NormalSkill = 7000;
    private const double Tolerance = 0.001;

    [Test]
    public async Task GetAttackDelay_WithoutAnySpeedRow_KeepsTheWeaponSpeeds()
    {
        // The exact-equality pin: 1500 melee, 1800 ranged, and the untouched
        // castTime + cooldown + additionalDelay sum for a normal skill.
        var character = CreateCharacter();

        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), character)).IsEqualTo(1500.0);
        await Assert.That(SkillManager.GetAttackDelay(Template(OffhandAutoAttack), character)).IsEqualTo(1500.0);
        await Assert.That(SkillManager.GetAttackDelay(Template(RangedAutoAttack), character)).IsEqualTo(1800.0);
        await Assert.That(SkillManager.GetAttackDelay(Template(NormalSkill), character)).IsEqualTo(3000.0);
    }

    [Test]
    public async Task GetAttackDelay_MeleeSpeedMul_PacesMeleeAndOffhandOnly()
    {
        // 55 is the ranged view of the same rating; a melee debuff must not slow a bow.
        var character = CreateCharacter();
        TestUnitModifier.Apply(character, UnitAttribute.MeleeSpeedMul, -666);

        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), character)).IsEqualTo(1500.0 * 2.99).Within(Tolerance);
        await Assert.That(SkillManager.GetAttackDelay(Template(OffhandAutoAttack), character)).IsEqualTo(1500.0 * 2.99).Within(Tolerance);
        await Assert.That(SkillManager.GetAttackDelay(Template(RangedAutoAttack), character)).IsEqualTo(1800.0);
    }

    [Test]
    public async Task GetAttackDelay_RangedSpeedMul_PacesTheRangedSwingOnly()
    {
        var character = CreateCharacter();
        TestUnitModifier.Apply(character, UnitAttribute.RangedSpeedMul, -500);

        await Assert.That(SkillManager.GetAttackDelay(Template(RangedAutoAttack), character)).IsEqualTo(1800.0 * 2.0).Within(Tolerance);
        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), character)).IsEqualTo(1500.0);
    }

    [Test]
    public async Task GetAttackDelay_AttackSpeedMul_PacesEveryAttack_AndTheRecoveryOfASkill()
    {
        var character = CreateCharacter();
        TestUnitModifier.Apply(character, UnitAttribute.AttackSpeedMul, 1000);

        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), character)).IsEqualTo(1500.0 * 0.5).Within(Tolerance);
        await Assert.That(SkillManager.GetAttackDelay(Template(RangedAutoAttack), character)).IsEqualTo(1800.0 * 0.5).Within(Tolerance);
        // Cast time is a casting_time_mul matter, so only the 1000 ms cooldown and the 1000 ms additional
        // delay halve: 1000 + 500 + 500.
        await Assert.That(SkillManager.GetAttackDelay(Template(NormalSkill), character)).IsEqualTo(2000.0).Within(Tolerance);
    }

    [Test]
    public async Task GetAttackDelay_AttackSpeedMul_OutranksThePerTypeRating()
    {
        var character = CreateCharacter();
        TestUnitModifier.Apply(character, UnitAttribute.MeleeSpeedMul, -666, buffIndex: 1);
        TestUnitModifier.Apply(character, UnitAttribute.AttackSpeedMul, 1000, buffIndex: 2);

        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), character)).IsEqualTo(1500.0 * 0.5).Within(Tolerance);
    }

    [Test]
    public async Task GetAttackDelay_ClipsAStackedRatingAtTheUnitAttributeLimits()
    {
        var fast = CreateCharacter();
        TestUnitModifier.Apply(fast, UnitAttribute.AttackSpeedMul, 4000); // clipped to the +2000 limit

        await Assert.That(SkillManager.GetAttackDelay(Template(NormalSkill), fast)).IsEqualTo(1660.0).Within(Tolerance);

        var slow = CreateCharacter();
        TestUnitModifier.Apply(slow, UnitAttribute.MeleeSpeedMul, -900); // clipped to the -666 limit

        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), slow)).IsEqualTo(1500.0 * 2.99).Within(Tolerance);
    }

    [Test]
    public async Task APercentStyleRow_ScalesWhatTheValueRowsAlreadyAdded()
    {
        // unit_modifier_type_id 1 adds a percentage of the value accumulated so far, the shape
        // Character.GlobalCooldownMul and the sibling *_damage_mul getters use: 500 plus 100% is 1000, and
        // the interval halves. A percent row on a unit with no value row has nothing to scale, which is why
        // the 8 percent-typed rows on 54 and the 13 on 218 read as no-ops here exactly as they do for 74.
        var withValueRow = CreateCharacter();
        TestUnitModifier.Apply(withValueRow, UnitAttribute.MeleeSpeedMul, 500, buffIndex: 1);
        TestUnitModifier.Apply(withValueRow, UnitAttribute.MeleeSpeedMul, 100, UnitModifierType.Percent, buffIndex: 2);

        await Assert.That(withValueRow.MeleeSpeedRating).IsEqualTo(1000L);
        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), withValueRow)).IsEqualTo(1500.0 * 0.5).Within(Tolerance);

        var percentOnly = CreateCharacter();
        TestUnitModifier.Apply(percentOnly, UnitAttribute.MeleeSpeedMul, 100, UnitModifierType.Percent);

        await Assert.That(percentOnly.MeleeSpeedRating).IsEqualTo(0L);
        await Assert.That(SkillManager.GetAttackDelay(Template(MeleeAutoAttack), percentOnly)).IsEqualTo(1500.0);
    }

    [Test]
    public async Task SpeedRatings_AreZeroWithoutARow_AndTheStoredSumWithOne()
    {
        var unit = new Unit { ObjId = 10 };

        await Assert.That(unit.MeleeSpeedRating).IsEqualTo(0L);
        await Assert.That(unit.RangedSpeedRating).IsEqualTo(0L);
        await Assert.That(unit.AttackSpeedRating).IsEqualTo(0L);
        await Assert.That(unit.AttackAnimSpeedRating).IsEqualTo(0L);

        TestUnitModifier.Apply(unit, UnitAttribute.MeleeSpeedMul, -666, buffIndex: 1);
        TestUnitModifier.Apply(unit, UnitAttribute.MeleeSpeedMul, -100, buffIndex: 2);

        await Assert.That(unit.MeleeSpeedRating).IsEqualTo(-766L);
        await Assert.That(unit.RangedSpeedRating).IsEqualTo(0L);
    }

    [Test]
    public async Task AttackAnimSpeedRating_FollowsTheAnimationRows()
    {
        var unit = new Unit { ObjId = 11 };
        TestUnitModifier.Apply(unit, UnitAttribute.AttackAnimSpeedMul, -666);

        await Assert.That(unit.AttackAnimSpeedRating).IsEqualTo(-666L);
        // The consumer: the fire-animation sync time keeps its GlobalCooldownMul factor without a 119 row
        // and takes the animation rating with one.
        await Assert.That(SpeedMultiplierRules.AnimationFactor(unit.AttackAnimSpeedRating, 1.0)).IsEqualTo(2.99).Within(1e-6);
    }

    [Test]
    public async Task RollCombatDice_WithoutAntiMissRows_KeepsThePlainAccuracyRoll()
    {
        // 100 accuracy is the shipped baseline, so 200 rolls must all hit — this is the absence pin for
        // 78/83/88.
        var attacker = new Unit { ObjId = 20, Level = 50 };
        var target = new Unit { ObjId = 21, Level = 50 };

        await Assert.That(attacker.MeleeAntiMissMul).IsEqualTo(1f);
        await Assert.That(attacker.RangedAntiMissMul).IsEqualTo(1f);
        await Assert.That(attacker.SpellAntiMissMul).IsEqualTo(1f);

        for (var i = 0; i < 200; i++)
            await Assert.That(Skill(DamageType.Melee).RollCombatDice(attacker, target)).IsEqualTo(SkillHitType.MeleeHit);
    }

    [Test]
    public async Task RollCombatDice_AnAntiMissRowOfMinus1000_NeverHits()
    {
        // A -1000 row zeroes the multiplier, so the 100-point baseline accuracy becomes 0 and every roll
        // misses.
        var attacker = new Unit { ObjId = 30, Level = 50 };
        var target = new Unit { ObjId = 31, Level = 50 };
        TestUnitModifier.Apply(attacker, UnitAttribute.MeleeAntiMissMul, -1000);

        await Assert.That(attacker.MeleeAntiMissMul).IsEqualTo(0f);

        for (var i = 0; i < 200; i++)
            await Assert.That(Skill(DamageType.Melee).RollCombatDice(attacker, target)).IsEqualTo(SkillHitType.MeleeMiss);
    }

    [Test]
    public async Task RollCombatDice_BlindStyleRow_LeavesAMixtureOfHitsAndMisses()
    {
        // Buff 26158 (실명) stores -700: 30% of the rolls hit. Over 200 rolls both outcomes appearing is
        // what tells the multiplier apart from "always" and "never".
        var attacker = new Unit { ObjId = 40, Level = 50 };
        var target = new Unit { ObjId = 41, Level = 50 };
        TestUnitModifier.Apply(attacker, UnitAttribute.SpellAntiMissMul, -700);

        var hits = 0;
        var misses = 0;
        for (var i = 0; i < 200; i++)
        {
            if (Skill(DamageType.Magic).RollCombatDice(attacker, target) == SkillHitType.SpellHit)
                hits++;
            else
                misses++;
        }

        await Assert.That(hits).IsGreaterThan(0);
        await Assert.That(misses).IsGreaterThan(0);
    }

    [Test]
    public async Task RollCombatDice_EachDamageTypeUsesItsOwnAntiMissRow()
    {
        var attacker = new Unit { ObjId = 50, Level = 50 };
        var target = new Unit { ObjId = 51, Level = 50 };
        TestUnitModifier.Apply(attacker, UnitAttribute.RangedAntiMissMul, -1000);

        await Assert.That(Skill(DamageType.Ranged).RollCombatDice(attacker, target)).IsEqualTo(SkillHitType.RangedMiss);
        await Assert.That(Skill(DamageType.Melee).RollCombatDice(attacker, target)).IsEqualTo(SkillHitType.MeleeHit);
    }

    [Test]
    public async Task RollCombatDice_Buff807sMinus3000Row_LeavesTheDebuffedCasterNoSpellHit()
    {
        // Buff 807 (주문 방해) stores -3000 on spell_anti_miss_mul, ten times the -300 its own description
        // ("시전 시간을 30% 지연시키고 마법 성공률을 30% 감소") and its sibling casting_time_mul row (71, 300)
        // both promise. The choice recorded in AntiMissRules is the floor rather than a rescale, so the
        // debuffed caster's SpellAntiMissMul is 0 and every spell roll misses for the buff's duration; its
        // melee accuracy, the row's own attribute being spell-only, is untouched.
        var attacker = new Unit { ObjId = 60, Level = 50 };
        var target = new Unit { ObjId = 61, Level = 50 };
        TestUnitModifier.Apply(attacker, UnitAttribute.SpellAntiMissMul, -3000);

        await Assert.That(attacker.SpellAntiMissMul).IsEqualTo(0f);
        await Assert.That(attacker.MeleeAntiMissMul).IsEqualTo(1f);

        for (var i = 0; i < 200; i++)
            await Assert.That(Skill(DamageType.Magic).RollCombatDice(attacker, target)).IsEqualTo(SkillHitType.SpellMiss);

        await Assert.That(Skill(DamageType.Melee).RollCombatDice(attacker, target)).IsEqualTo(SkillHitType.MeleeHit);
    }

    private static SkillTemplate Template(uint id) => new()
    {
        Id = id,
        CastingTime = 1000,
        CooldownTime = 1000
    };

    private static Skill Skill(DamageType damageType) => new()
    {
        Template = new SkillTemplate { Id = 1, DamageTypeId = (uint)damageType },
        Level = 1
    };

    private static CharacterMock CreateCharacter() => new() { ObjId = 100, Level = 50 };
}
