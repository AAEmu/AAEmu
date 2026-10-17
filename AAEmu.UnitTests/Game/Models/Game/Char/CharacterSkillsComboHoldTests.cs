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
/// A Combo follow-up is the next link after the last accepted cast, inside
/// value2 plus that hit's cast/GCD wait. Holding the root does not open later links on their own.
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
    public async Task InstantLink_WindowIsValue2Only()
    {
        var character = NewCharacter();
        character.Skills.NoteSkillFired(Second);

        await Assert.That(SkillComboRules.ComboWindowMs(Second)).IsEqualTo(1000);
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
    public async Task NothingFired_RefusesTheFollowUps()
    {
        var character = NewCharacter();

        await Assert.That(character.Skills.HasSkill(Second)).IsFalse();
        await Assert.That(character.Skills.HasSkill(Third)).IsFalse();
    }

    private static void Backdate(Character character, int millisecondsAgo)
    {
        typeof(CharacterSkills).GetField("_lastComboAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character.Skills, DateTime.UtcNow.AddMilliseconds(-millisecondsAgo));
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
