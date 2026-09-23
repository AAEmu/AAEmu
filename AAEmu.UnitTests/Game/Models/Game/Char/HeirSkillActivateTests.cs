using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Heirs;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// Choosing an ancestral successor learns its base skill when that base is missing, and refuses
/// when the ordinary learn path refuses. Ids here are invented.
/// </summary>
[NotInParallel]
public class HeirSkillActivateTests
{
    private const uint HeirRowId = 91001;
    private const uint BaseSkillId = 91002;
    private const uint SuccessorSkillId = 91003;

    private Dictionary<uint, HeirSkill> _savedHeirRows;
    private Dictionary<uint, HeirSkillDetail> _savedSuccessors;
    private Dictionary<uint, SkillTemplate> _savedSkills;

    [Before(Test)]
    public void SaveCatalogs()
    {
        _savedHeirRows = Rows();
        _savedSuccessors = Successors();
        _savedSkills = Skills();
        Set(HeirGameData.Instance, "_skillsById", new Dictionary<uint, HeirSkill>());
        Set(HeirGameData.Instance, "_successorsBySkillId", new Dictionary<uint, HeirSkillDetail>());
        Set(SkillManager.Instance, "_skills", new Dictionary<uint, SkillTemplate>());
        typeof(HeirGameData).GetProperty(nameof(HeirGameData.StartLevel))!
            .SetValue(HeirGameData.Instance, (byte)0);
    }

    [After(Test)]
    public void RestoreCatalogs()
    {
        Set(HeirGameData.Instance, "_skillsById", _savedHeirRows);
        Set(HeirGameData.Instance, "_successorsBySkillId", _savedSuccessors);
        Set(SkillManager.Instance, "_skills", _savedSkills);
    }

    [Test]
    public async Task Activate_MissingBase_LearnsItThenSelectsTheSuccessor()
    {
        var character = CharacterWith(baseCost: 0);
        var persisted = false;
        character.HeirSkills.PersistForTest = (_, _) =>
        {
            persisted = true;
            return true;
        };

        var activated = character.HeirSkills.TryActivate(HeirRowId, SuccessorSkillId, isChange: false);

        await Assert.That(activated).IsTrue();
        await Assert.That(persisted).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(BaseSkillId)).IsTrue();
        await Assert.That(character.HeirSkills.IsActiveSuccessor(SuccessorSkillId)).IsTrue();
    }

    [Test]
    public async Task Activate_MissingBaseWithoutPoints_RefusesAndLearnsNothing()
    {
        var character = CharacterWith(baseCost: 5);
        var persisted = false;
        character.HeirSkills.PersistForTest = (_, _) =>
        {
            persisted = true;
            return true;
        };

        var activated = character.HeirSkills.TryActivate(HeirRowId, SuccessorSkillId, isChange: false);

        await Assert.That(activated).IsFalse();
        await Assert.That(persisted).IsFalse();
        await Assert.That(character.Skills.Skills.ContainsKey(BaseSkillId)).IsFalse();
    }

    private Character CharacterWith(int baseCost)
    {
        var row = new HeirSkill { Id = HeirRowId, SkillId = BaseSkillId, Step = 0, Enable = true };
        var successor = new HeirSkillDetail
        {
            Id = 1,
            HeirSkillId = HeirRowId,
            SkillId = SuccessorSkillId,
            SkillActiveTypeId = SkillActiveType.Active
        };
        Rows()[HeirRowId] = row;
        Successors()[SuccessorSkillId] = successor;
        Skills()[BaseSkillId] = new SkillTemplate
        {
            Id = BaseSkillId,
            AbilityId = AbilityType.General,
            SkillPoints = baseCost
        };
        Skills()[SuccessorSkillId] = new SkillTemplate
        {
            Id = SuccessorSkillId,
            AbilityId = AbilityType.General
        };

        var character = new Character(new UnitCustomModelParams()) { Level = 1 };
        character.Skills = new CharacterSkills(character);
        character.SkillActiveTypes = new CharacterSkillActiveTypes(character);
        character.HeirSkills = new CharacterHeirSkills(character);
        return character;
    }

    private static Dictionary<uint, HeirSkill> Rows() =>
        (Dictionary<uint, HeirSkill>)Get(HeirGameData.Instance, "_skillsById");

    private static Dictionary<uint, HeirSkillDetail> Successors() =>
        (Dictionary<uint, HeirSkillDetail>)Get(HeirGameData.Instance, "_successorsBySkillId");

    private static Dictionary<uint, SkillTemplate> Skills() =>
        (Dictionary<uint, SkillTemplate>)Get(SkillManager.Instance, "_skills");

    private static object Get(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
