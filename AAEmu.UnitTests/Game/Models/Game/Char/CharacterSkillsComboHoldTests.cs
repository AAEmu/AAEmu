using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// A Combo follow-up opens when its prior link is accepted, for value2 plus that hit's
/// cast/GCD wait. Holding the root does not open later links on their own. Intervening
/// casts leave other open windows alone (dash → attack → return).
/// Ids here are invented.
/// </summary>
[NotInParallel]
public class CharacterSkillsComboHoldTests
{
    private const uint Root = 92001;
    private const uint Second = 92002;
    private const uint Third = 92003;
    private const uint GcdRoot = 92004;
    private const uint GcdSecond = 92005;
    private const uint Unrelated = 92006;

    private SingletonScope<SkillManager> _skills;

    [Before(Test)]
    public void InstallCatalog()
    {
        _skills = new SingletonScope<SkillManager>(new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object));
        var skills = new Dictionary<uint, SkillTemplate>
        {
            [Root] = Chained(Root, Second, castingTime: 1000),
            [Second] = Chained(Second, Third, castingTime: 0),
            [Third] = new SkillTemplate { Id = Third, AbilityId = AbilityType.Magic },
            // Plot-only caster chain: value2 1000 but custom_gcd 1200 (Serpent Glare / Crashing Wave).
            [GcdRoot] = Chained(GcdRoot, GcdSecond, castingTime: 0, customGcd: 1200),
            [GcdSecond] = new SkillTemplate { Id = GcdSecond, AbilityId = AbilityType.Magic },
            [Unrelated] = new SkillTemplate { Id = Unrelated, AbilityId = AbilityType.Fight },
        };
        typeof(SkillManager).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(SkillManager.Instance, skills);
    }

    [After(Test)]
    public void RestoreCatalog() => _skills.Dispose();

    [Test]
    public async Task LearnedRoot_DoesNotHoldFollowUpsWithoutAPriorCast()
    {
        var character = NewCharacter();
        character.Skills.AddSkill(SkillManager.Instance.GetSkillTemplate(Root), 1, packet: false);

        await Assert.That(character.Skills.HasSkill(Second)).IsFalse();
        await Assert.That(character.Skills.HasSkill(Third)).IsFalse();
    }

    [Test]
    public async Task CastTimeRoot_KeepsTheNextLinkOpenThroughTheCast()
    {
        var character = NewCharacter();
        character.Skills.NoteSkillFired(Root);

        // value2 is 1000 and the root casts for 1000, so a follow-up that arrives as the cast ends
        // is still inside the window. The link after that is not.
        await Assert.That(SkillComboRules.ComboWindowMs(Root)).IsEqualTo(2000);
        Backdate(character, millisecondsAgo: 1500);
        await Assert.That(character.Skills.HasSkill(Second)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Third)).IsFalse();
    }

    [Test]
    public async Task Window_ClosesAfterCastTimePlusValue2()
    {
        var character = NewCharacter();
        character.Skills.NoteSkillFired(Root);
        Backdate(character, millisecondsAgo: 2001);

        await Assert.That(character.Skills.HasSkill(Second)).IsFalse();
    }

    [Test]
    public async Task InstantLink_WindowIncludesDefaultSharedGcd()
    {
        var character = NewCharacter();
        character.Skills.NoteSkillFired(Second);

        // value2 1000 + player default shared GCD 1000 (no custom_gcd / weapon slot).
        await Assert.That(SkillComboRules.ComboWindowMs(Second)).IsEqualTo(2000);
        Backdate(character, millisecondsAgo: 500);
        await Assert.That(character.Skills.HasSkill(Third)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Root)).IsFalse();
    }

    [Test]
    public async Task CustomGcdLongerThanValue2_KeepsTheNextLinkOpenThroughTheGcd()
    {
        var character = NewCharacter();
        character.Skills.NoteSkillFired(GcdRoot);

        // value2 1000 + custom_gcd 1200 — the client cannot send until the GCD ends.
        await Assert.That(SkillComboRules.ComboWindowMs(GcdRoot)).IsEqualTo(2200);
        Backdate(character, millisecondsAgo: 1500);
        await Assert.That(character.Skills.HasSkill(GcdSecond)).IsTrue();

        Backdate(character, millisecondsAgo: 2201);
        await Assert.That(character.Skills.HasSkill(GcdSecond)).IsFalse();
    }

    [Test]
    public async Task CustomGcd_ScalesByEquippedWeaponSpeedWhenWeaponGcdIdSet()
    {
        // With default_gcd unset, custom_gcd × (equipped speed / 1000) is the shared lock.
        var withWeaponClass = new SkillTemplate
        {
            CastingTime = 0, CustomGcd = 250, WeaponGcdId = 15, DefaultGcd = false
        };
        var noWeaponClass = new SkillTemplate
        {
            CastingTime = 0, CustomGcd = 250, WeaponGcdId = 0, DefaultGcd = false
        };

        await Assert.That(SkillComboRules.FollowUpReadyDelayMs(withWeaponClass, equippedWeaponGcdSpeed: 1300))
            .IsEqualTo(325);
        await Assert.That(SkillComboRules.FollowUpReadyDelayMs(withWeaponClass, equippedWeaponGcdSpeed: 1100))
            .IsEqualTo(275);
        await Assert.That(SkillComboRules.FollowUpReadyDelayMs(noWeaponClass, equippedWeaponGcdSpeed: 1300))
            .IsEqualTo(250);
    }

    [Test]
    public async Task EquippedWeaponGcd_DecidesReadyDelayWhenCustomIsUnset()
    {
        var weaponPaced = new SkillTemplate
        {
            CastingTime = 0,
            CustomGcd = 0,
            DefaultGcd = false,
            WeaponGcdId = 15
        };

        await Assert.That(SkillComboRules.FollowUpReadyDelayMs(weaponPaced, equippedWeaponGcdSpeed: 1100))
            .IsEqualTo(1100);
        // Empty slot + no custom → player default shared GCD.
        await Assert.That(SkillComboRules.FollowUpReadyDelayMs(weaponPaced, equippedWeaponGcdSpeed: 0))
            .IsEqualTo(1000);
    }

    [Test]
    public async Task CustomGcd_ScalesComboWindowEvenWhenDefaultGcdSet()
    {
        // Whirlwind / Triple Slash shape: default_gcd + custom_gcd + weapon slot → custom × speed / 1000.
        var hit = Chained(Second, Third, castingTime: 0, customGcd: 700);
        hit.WeaponGcdId = 15;
        hit.DefaultGcd = true;
        typeof(SkillManager).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(SkillManager.Instance, new Dictionary<uint, SkillTemplate>
            {
                [Second] = hit,
                [Third] = new SkillTemplate { Id = Third, AbilityId = AbilityType.Magic },
            });

        // value2 1000 + ready delay (700×800/1000=560) → 1560; 1100 → 1770; empty slot → 1700.
        await Assert.That(SkillComboRules.ComboWindowMs(Second, equippedWeaponGcdSpeed: 800)).IsEqualTo(1560);
        await Assert.That(SkillComboRules.ComboWindowMs(Second, equippedWeaponGcdSpeed: 1100)).IsEqualTo(1770);
        await Assert.That(SkillComboRules.ComboWindowMs(Second, equippedWeaponGcdSpeed: 0)).IsEqualTo(1700);
    }

    [Test]
    public async Task CastTimeMul_ScalesCastOnly_NotSharedGcd()
    {
        // Cast must outrun the player default shared GCD (1000) or Max(cast, gcd) hides the cast mul.
        var castHit = new SkillTemplate { CastingTime = 2000, CustomGcd = 0, DefaultGcd = false };
        var gcdHit = new SkillTemplate { CastingTime = 0, CustomGcd = 1000, WeaponGcdId = 0 };

        await Assert.That(SkillComboRules.FollowUpReadyDelayMs(castHit, castTimeMul: 0.8f)).IsEqualTo(1600);
        await Assert.That(SkillComboRules.FollowUpReadyDelayMs(gcdHit, castTimeMul: 0.8f)).IsEqualTo(1000);
    }

    [Test]
    public async Task NothingFired_RefusesTheFollowUps()
    {
        var character = NewCharacter();

        await Assert.That(character.Skills.HasSkill(Second)).IsFalse();
        await Assert.That(character.Skills.HasSkill(Third)).IsFalse();
    }

    [Test]
    public async Task InterveningCast_DoesNotCloseAnOpenFollowUpWindow()
    {
        var character = NewCharacter();
        character.Skills.NoteSkillFired(Root);
        character.Skills.NoteSkillFired(Unrelated);

        await Assert.That(character.Skills.HasSkill(Second)).IsTrue();
    }

    [Test]
    public async Task AcceptingTheFollowUp_OpensTheNextLinkAndConsumesThisOne()
    {
        var character = NewCharacter();
        character.Skills.NoteSkillFired(Root);
        character.Skills.NoteSkillFired(Second);

        await Assert.That(character.Skills.HasSkill(Third)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Second)).IsFalse();
    }

    private static void Backdate(Character character, int millisecondsAgo)
    {
        var field = typeof(CharacterSkills).GetField("_comboFollowUpExpires",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var expires = (Dictionary<uint, DateTime>)field.GetValue(character.Skills)!;
        foreach (var id in expires.Keys.ToList())
            expires[id] = expires[id].AddMilliseconds(-millisecondsAgo);
    }

    private static SkillTemplate Chained(uint id, uint next, int castingTime, int customGcd = 0) => new()
    {
        Id = id,
        AbilityId = AbilityType.Magic,
        CastingTime = castingTime,
        CustomGcd = customGcd,
        Effects =
        [
            new SkillEffect
            {
                Template = new SpecialEffect
                {
                    SpecialEffectTypeId = SpecialType.Combo,
                    Value1 = (int)next,
                    Value2 = 1000
                }
            }
        ]
    };

    private static Character NewCharacter()
    {
        var character = new Character(new UnitCustomModelParams()) { Level = 1 };
        character.Skills = new CharacterSkills(character);
        return character;
    }
}
