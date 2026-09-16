using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class CombatDiceRulesTests
{
    [Test]
    public async Task Kind_ComesFromCombatDiceId()
    {
        await Assert.That(CombatDiceRules.Kind(1, 1)).IsEqualTo(CombatDiceKind.Melee);
        await Assert.That(CombatDiceRules.Kind(2, 4)).IsEqualTo(CombatDiceKind.Ranged);
        await Assert.That(CombatDiceRules.Kind(3, 2)).IsEqualTo(CombatDiceKind.Spell);
        await Assert.That(CombatDiceRules.Kind(4, 1)).IsEqualTo(CombatDiceKind.AlwaysHit);
        await Assert.That(CombatDiceRules.Kind(5, 1)).IsEqualTo(CombatDiceKind.MeleeUndefendable);
        await Assert.That(CombatDiceRules.Kind(6, 2)).IsEqualTo(CombatDiceKind.Heal);
        await Assert.That(CombatDiceRules.Kind(7, 1)).IsEqualTo(CombatDiceKind.EachEffectRollDice);
        await Assert.That(CombatDiceRules.Kind(8, 4)).IsEqualTo(CombatDiceKind.RangeUndefendable);
    }

    [Test]
    public async Task Kind_UnsetFallsBackToTheDamageType()
    {
        await Assert.That(CombatDiceRules.Kind(0, 1)).IsEqualTo(CombatDiceKind.Melee);
        await Assert.That(CombatDiceRules.Kind(0, 4)).IsEqualTo(CombatDiceKind.Ranged);
        await Assert.That(CombatDiceRules.Kind(0, 2)).IsEqualTo(CombatDiceKind.Spell);
        // Siege and heal have no dice kind of their own and roll nothing.
        await Assert.That(CombatDiceRules.Kind(0, 3)).IsEqualTo(CombatDiceKind.AlwaysHit);
        await Assert.That(CombatDiceRules.Kind(0, 5)).IsEqualTo(CombatDiceKind.AlwaysHit);
    }

    [Test]
    public async Task Avoidance_RollsForTheThreeFamiliesOnly()
    {
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.Melee)).IsTrue();
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.Ranged)).IsTrue();
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.Spell)).IsTrue();
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.AlwaysHit)).IsFalse();
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.MeleeUndefendable)).IsFalse();
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.RangeUndefendable)).IsFalse();
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.Heal)).IsFalse();
        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceKind.EachEffectRollDice)).IsFalse();
    }

    [Test]
    public async Task Miss_RollsForTheUndefendableKindsButNotForAlwaysHitOrHeal()
    {
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceKind.Melee)).IsTrue();
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceKind.Ranged)).IsTrue();
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceKind.Spell)).IsTrue();
        // Undefendable means "cannot be dodged, parried or blocked", not "cannot miss".
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceKind.MeleeUndefendable)).IsTrue();
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceKind.RangeUndefendable)).IsTrue();
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceKind.AlwaysHit)).IsFalse();
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceKind.Heal)).IsFalse();
    }

    [Test]
    public async Task RollsForCast_ExtendsToAnyTargetTypeCarryingDamage()
    {
        // Hostile skills always rolled.
        await Assert.That(CombatDiceRules.RollsForCast(true, false)).IsTrue();
        // A damage skill with another target type did not roll at all before.
        await Assert.That(CombatDiceRules.RollsForCast(false, true)).IsTrue();
        // A pure utility skill has nothing to roll for.
        await Assert.That(CombatDiceRules.RollsForCast(false, false)).IsFalse();
    }

    [Test]
    public async Task HitAndMissTypes_FollowTheDamageType()
    {
        await Assert.That(CombatDiceRules.HitTypeFor(1)).IsEqualTo(SkillHitType.MeleeHit);
        await Assert.That(CombatDiceRules.HitTypeFor(2)).IsEqualTo(SkillHitType.SpellHit);
        await Assert.That(CombatDiceRules.HitTypeFor(4)).IsEqualTo(SkillHitType.RangedHit);
        // Siege uses the ranged flag: the client has no siege hit type.
        await Assert.That(CombatDiceRules.HitTypeFor(3)).IsEqualTo(SkillHitType.RangedHit);

        await Assert.That(CombatDiceRules.MissTypeFor(1)).IsEqualTo(SkillHitType.MeleeMiss);
        await Assert.That(CombatDiceRules.MissTypeFor(2)).IsEqualTo(SkillHitType.SpellMiss);
        await Assert.That(CombatDiceRules.MissTypeFor(4)).IsEqualTo(SkillHitType.RangedMiss);
    }

    [Test]
    public async Task UnrollableSource_KeepsTheOldOutcomePerDamageType()
    {
        // A source that is not a Unit ended in the miss branch for the three rollable families.
        await Assert.That(CombatDiceRules.UnrollableSourceType(1)).IsEqualTo(SkillHitType.MeleeMiss);
        await Assert.That(CombatDiceRules.UnrollableSourceType(2)).IsEqualTo(SkillHitType.SpellMiss);
        await Assert.That(CombatDiceRules.UnrollableSourceType(4)).IsEqualTo(SkillHitType.RangedMiss);
        // Siege has no miss type and reported a hit; heal reported nothing at all.
        await Assert.That(CombatDiceRules.UnrollableSourceType(3)).IsEqualTo(SkillHitType.RangedHit);
        await Assert.That(CombatDiceRules.UnrollableSourceType(5)).IsEqualTo(SkillHitType.Invalid);
    }

    [Test]
    public async Task SkillMissedFor_CountsTheAvoidanceAndMissResults()
    {
        await Assert.That(Skill.SkillMissedFor(SkillHitType.MeleeMiss)).IsTrue();
        await Assert.That(Skill.SkillMissedFor(SkillHitType.SpellMiss)).IsTrue();
        await Assert.That(Skill.SkillMissedFor(SkillHitType.RangedBlock)).IsTrue();
        await Assert.That(Skill.SkillMissedFor(SkillHitType.MeleeParry)).IsTrue();
        await Assert.That(Skill.SkillMissedFor(SkillHitType.Immune)).IsTrue();
        await Assert.That(Skill.SkillMissedFor(SkillHitType.MeleeHit)).IsFalse();
        await Assert.That(Skill.SkillMissedFor(SkillHitType.MeleeCritical)).IsFalse();
        await Assert.That(Skill.SkillMissedFor(SkillHitType.Invalid)).IsFalse();
    }

    [Test]
    public async Task AlwaysHitKind_NeverMisses()
    {
        // 33,871 skills carry the DB default combat_dice_id 4. A high dodge rate on the target cannot
        // make the roll miss because no roll happens for this kind.
        var rule = CombatDiceRules.Kind(4, 1);
        await Assert.That(CombatDiceRules.RollsAvoidance(rule)).IsFalse();
        await Assert.That(CombatDiceRules.RollsMiss(rule)).IsFalse();
        await Assert.That(CombatDiceRules.HitTypeFor(1)).IsEqualTo(SkillHitType.MeleeHit);
        await Assert.That(Skill.SkillMissedFor(SkillHitType.MeleeHit)).IsFalse();
    }

    [Test]
    public async Task AlwaysHitSkill_LandsEveryTime_AgainstAnUnmissableTarget()
    {
        // The same check through the real roll, against a target that dodges, blocks and parries
        // everything and an attacker with no accuracy at all.
        var attacker = new Unit { MeleeAccuracy = 0f, SpellAccuracy = 0f, RangedAccuracy = 0f };
        var target = new Unit
        {
            DodgeRate = 100f,
            BlockRate = 100f,
            MeleeParryRate = 100f,
            RangedParryRate = 100f
        };
        var skill = new Skill(new SkillTemplate { Id = 10025, DamageTypeId = 1, CombatDiceId = 4 });

        for (var roll = 0; roll < 1000; roll++)
        {
            var result = skill.RollCombatDice(attacker, target);
            if (Skill.SkillMissedFor(result))
                throw new Exception($"always_hit skill missed on roll {roll} with {result}");
        }

        await Assert.That(Skill.SkillMissedFor(skill.RollCombatDice(attacker, target))).IsFalse();
    }

    [Test]
    public async Task MeleeKind_StillMisses_AgainstTheSameTarget()
    {
        // The control for the test above: the identical setup with kind 1 does miss.
        var attacker = new Unit { MeleeAccuracy = 0f, SpellAccuracy = 0f, RangedAccuracy = 0f };
        var target = new Unit
        {
            DodgeRate = 100f,
            BlockRate = 100f,
            MeleeParryRate = 100f,
            RangedParryRate = 100f
        };
        var skill = new Skill(new SkillTemplate { Id = 10025, DamageTypeId = 1, CombatDiceId = 1 });

        await Assert.That(CombatDiceRules.RollsAvoidance(CombatDiceRules.Kind(1, 1))).IsTrue();
        await Assert.That(CombatDiceRules.RollsMiss(CombatDiceRules.Kind(1, 1))).IsTrue();
        // Positioned at the origin facing each other, the avoidance roll runs.
        var result = skill.RollCombatDice(attacker, target);
        await Assert.That(Skill.SkillMissedFor(result)).IsTrue();
    }
}
