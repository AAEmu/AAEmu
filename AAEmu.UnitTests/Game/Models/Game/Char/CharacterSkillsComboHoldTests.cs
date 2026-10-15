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
/// A Combo follow-up (special type 48) is castable whenever the character holds an earlier hit of
/// the same chain â€” learned, buff-granted or a selected Heir successor â€” without a prior cast
/// having to land inside the previous link's window. Ids here are invented.
/// </summary>
[NotInParallel]
public class CharacterSkillsComboHoldTests
{
    private const uint Root = 92001;
    private const uint Second = 92002;
    private const uint Third = 92003;
    private const uint Unrelated = 92099;

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
            [Unrelated] = new SkillTemplate { Id = Unrelated, AbilityId = AbilityType.Magic },
        };
        typeof(SkillManager).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(SkillManager.Instance, skills);
    }

    [After(Test)]
    public void RestoreCatalog() => _skills.Dispose();

    [Test]
    public async Task LearnedRoot_HoldsEveryLaterHit_WithoutAPriorCast()
    {
        var character = NewCharacter();
        character.Skills.AddSkill(SkillManager.Instance.GetSkillTemplate(Root), 1, packet: false);

        // No NoteSkillFired: a cast-time root's second hit arrives after the cast, outside the window.
        await Assert.That(character.Skills.HasSkill(Root)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Second)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Third)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Unrelated)).IsFalse();
    }

    [Test]
    public async Task HoldingOnlyAMiddleHit_HoldsWhatFollowsIt_NotTheRoot()
    {
        var character = NewCharacter();
        character.Skills.AddSkill(SkillManager.Instance.GetSkillTemplate(Second), 1, packet: false);

        await Assert.That(character.Skills.HasSkill(Third)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Root)).IsFalse();
    }

    [Test]
    public async Task BuffGrantedRoot_HoldsItsFollowUps()
    {
        var character = NewCharacter();
        character.Skills.TemporarySkills[Root] = new Skill(SkillManager.Instance.GetSkillTemplate(Root));

        await Assert.That(character.Skills.HasSkill(Second)).IsTrue();
        await Assert.That(character.Skills.HasSkill(Third)).IsTrue();
    }

    [Test]
    public async Task NothingHeld_RefusesTheFollowUps()
    {
        var character = NewCharacter();

        await Assert.That(character.Skills.HasSkill(Second)).IsFalse();
        await Assert.That(character.Skills.HasSkill(Third)).IsFalse();
    }

    [Test]
    public async Task ReplacedRoot_NoLongerHoldsItsFollowUps()
    {
        var character = NewCharacter();
        character.Skills.AddSkill(SkillManager.Instance.GetSkillTemplate(Root), 1, packet: false);
        character.Skills.ReplacedSkillIds.Add(Root);

        await Assert.That(character.Skills.HasSkill(Second)).IsFalse();
    }

    private static SkillTemplate Chained(uint id, uint next, int castingTime) => new()
    {
        Id = id,
        AbilityId = AbilityType.Magic,
        CastingTime = castingTime,
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
