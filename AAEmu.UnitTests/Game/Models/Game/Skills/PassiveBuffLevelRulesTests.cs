using System.Reflection;

using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>passive_buffs.level</c> and <c>passive_buffs.active</c>, and the level-up re-evaluation that reads
/// them. The shapes are the shipped ones: 11 of the 279 rows carry level 40-60 (the Predator/Trooper
/// chains and two level-60 general passives), five rows are <c>active='t'</c>, and a passive's bonuses
/// were applied with the <c>Buff.AbLevel</c> default of 1 for the whole life of the character.
/// </summary>
[NotInParallel]
public class PassiveBuffLevelRulesTests
{
    private const uint LevelFortyPassiveId = 293;         // shpped row: level 40, Predator, not active
    private const uint LevelFortyPassiveBuffId = 918385;
    private const uint ActivePassiveId = 369;             // shipped row: level 1, general, active
    private const uint ActivePassiveBuffId = 926210;
    private const uint ActiveLevelFortyPassiveId = 9001;  // level 40 active row, for the level gate
    private const uint ActiveLevelFortyPassiveBuffId = 9002;
    private const uint AlwaysPassiveId = 110;             // shipped row: level 1, general, not active
    private const uint AlwaysPassiveBuffId = 934590;

    private SingletonScope<SkillManager> _skills;
    private SingletonScope<BuffGameData> _buffGameData;
    private SingletonScope<ExperienceManager> _experience;

    [Before(Test)]
    public void InstallContentLookups()
    {
        WorldIntegration.ZoneAuthority = false;

        var manager = TestManagers.CreateSkillManager();
        SetField(manager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [LevelFortyPassiveBuffId] = new BuffTemplate { Id = LevelFortyPassiveBuffId },
            [ActivePassiveBuffId] = new BuffTemplate { Id = ActivePassiveBuffId },
            [ActiveLevelFortyPassiveBuffId] = new BuffTemplate { Id = ActiveLevelFortyPassiveBuffId },
            [AlwaysPassiveBuffId] = new BuffTemplate { Id = AlwaysPassiveBuffId }
        });
        SetField(manager, "_passiveBuffs", new Dictionary<uint, PassiveBuffTemplate>
        {
            [LevelFortyPassiveId] = new PassiveBuffTemplate
            {
                Id = LevelFortyPassiveId, Level = 40, AbilityId = AbilityType.Predator,
                BuffId = LevelFortyPassiveBuffId
            },
            [ActivePassiveId] = new PassiveBuffTemplate
            {
                Id = ActivePassiveId, Level = 1, AbilityId = AbilityType.General,
                BuffId = ActivePassiveBuffId, Active = true
            },
            [ActiveLevelFortyPassiveId] = new PassiveBuffTemplate
            {
                Id = ActiveLevelFortyPassiveId, Level = 40, AbilityId = AbilityType.General,
                BuffId = ActiveLevelFortyPassiveBuffId, Active = true
            },
            [AlwaysPassiveId] = new PassiveBuffTemplate
            {
                Id = AlwaysPassiveId, Level = 1, AbilityId = AbilityType.General,
                BuffId = AlwaysPassiveBuffId
            }
        });
        SetField(manager, "_buffGrants", new Dictionary<uint, BuffGrantSet>());
        SetField(manager, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(manager, "_buffBreakers", new Dictionary<uint, List<uint>>());

        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffGameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(buffGameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());

        _skills = new SingletonScope<SkillManager>(manager);
        _buffGameData = new SingletonScope<BuffGameData>(buffGameData);
        _experience = new SingletonScope<ExperienceManager>(CreateExperienceManager());
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _experience.Dispose();
        _buffGameData.Dispose();
        _skills.Dispose();
        WorldIntegration.ZoneAuthority = false;
    }

    [Test]
    public async Task CanLearn_LevelFortyPassive_IsRefusedAtThirtyNine()
    {
        var template = SkillManager.Instance.GetPassiveBuffTemplate(LevelFortyPassiveId);

        await Assert.That(PassiveBuffLevelRules.CanLearn(template, 39)).IsFalse();
        await Assert.That(PassiveBuffLevelRules.CanLearn(template, 40)).IsTrue();
        await Assert.That(PassiveBuffLevelRules.CanLearn(template, 55)).IsTrue();
    }

    [Test]
    public async Task MeetsLevel_LevelOneRow_PassesForEveryCharacter()
    {
        // 268 of the 279 rows ship level 1, which every character has reached.
        await Assert.That(PassiveBuffLevelRules.MeetsLevel(1, 1)).IsTrue();
        await Assert.That(PassiveBuffLevelRules.MeetsLevel(0, 1)).IsTrue();
    }

    [Test]
    public async Task AddBuff_LevelFortyPassive_LearnsItOnlyAtForty()
    {
        var character = CreateCharacter(level: 39);
        // The row is a Predator passive, so the tree check needs the character to have picked it.
        character.Ability1 = AbilityType.Predator;

        character.Skills.AddBuff(LevelFortyPassiveId, notify: false);

        await Assert.That(character.Skills.PassiveBuffs).IsEmpty();
        await Assert.That(character.Buffs.CheckBuff(LevelFortyPassiveBuffId)).IsFalse();

        character.Level = 40;
        character.Skills.AddBuff(LevelFortyPassiveId, notify: false);

        await Assert.That(character.Skills.PassiveBuffs.ContainsKey(LevelFortyPassiveId)).IsTrue();
        await Assert.That(character.Buffs.CheckBuff(LevelFortyPassiveBuffId)).IsTrue();
    }

    [Test]
    public async Task AutoGranted_OnlyTheActiveRowsTheCharacterReached()
    {
        var templates = new[]
        {
            SkillManager.Instance.GetPassiveBuffTemplate(LevelFortyPassiveId),
            SkillManager.Instance.GetPassiveBuffTemplate(ActivePassiveId),
            SkillManager.Instance.GetPassiveBuffTemplate(ActiveLevelFortyPassiveId),
            SkillManager.Instance.GetPassiveBuffTemplate(AlwaysPassiveId)
        };

        var atOne = PassiveBuffLevelRules.AutoGranted(templates, 1, new HashSet<uint>());
        var atForty = PassiveBuffLevelRules.AutoGranted(templates, 40, new HashSet<uint>());
        var alreadyHeld = PassiveBuffLevelRules.AutoGranted(templates, 40, new HashSet<uint> { ActivePassiveId });

        await Assert.That(atOne).IsEquivalentTo(new[] { ActivePassiveId });
        await Assert.That(atForty).IsEquivalentTo(new[] { ActivePassiveId, ActiveLevelFortyPassiveId });
        // A row that is not active is never handed out by the game, however old the character is.
        await Assert.That(atForty).DoesNotContain(AlwaysPassiveId);
        await Assert.That(atForty).DoesNotContain(LevelFortyPassiveId);
        await Assert.That(alreadyHeld).DoesNotContain(ActivePassiveId);
        await Assert.That(alreadyHeld).Contains(ActiveLevelFortyPassiveId);
    }

    [Test]
    public async Task ReevaluatePassivesOnLevelUp_LearnsTheActiveRowOnceTheLevelIsReached()
    {
        var character = CreateCharacter(level: 1);

        character.Skills.ReevaluatePassivesOnLevelUp();
        await Assert.That(character.Skills.PassiveBuffs.ContainsKey(ActivePassiveId)).IsTrue();
        await Assert.That(character.Skills.PassiveBuffs.ContainsKey(ActiveLevelFortyPassiveId)).IsFalse();

        // The level-up itself calls this (Character.ApplyLevelUpBenefits); the active level-40 row is only
        // picked up once the character has reached 40.
        character.Level = 40;
        character.Skills.ReevaluatePassivesOnLevelUp();

        await Assert.That(character.Skills.PassiveBuffs.ContainsKey(ActiveLevelFortyPassiveId)).IsTrue();
        await Assert.That(character.Buffs.CheckBuff(ActiveLevelFortyPassiveBuffId)).IsTrue();
        // The level-1 active row is not learned a second time.
        await Assert.That(character.Skills.PassiveBuffs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ReevaluatePassivesOnLevelUp_RebuildsTheLearnedPassiveAtTheNewLevel()
    {
        var character = CreateCharacter(level: 1);
        character.Skills.AddBuff(AlwaysPassiveId, notify: false);
        var applied = character.Buffs.GetEffectFromBuffId(AlwaysPassiveBuffId);

        await Assert.That(applied).IsNotNull();
        await Assert.That(applied.AbLevel).IsEqualTo(1u);

        character.Level = 42;
        character.Skills.ReevaluatePassivesOnLevelUp();

        // A general passive reads the character level, so the rebuilt instance carries the new one and is a
        // fresh object: the bonus values are snapshotted in BuffTemplate.Start.
        var rebuilt = character.Buffs.GetEffectFromBuffId(AlwaysPassiveBuffId);
        await Assert.That(rebuilt).IsNotNull();
        await Assert.That(rebuilt).IsNotSameReferenceAs(applied);
        await Assert.That(rebuilt.AbLevel).IsEqualTo(42u);
        await Assert.That(character.Skills.PassiveBuffs.ContainsKey(AlwaysPassiveId)).IsTrue();
    }

    [Test]
    public async Task AbLevelFor_NeverGoesBelowOne()
    {
        await Assert.That(PassiveBuffLevelRules.AbLevelFor(0)).IsEqualTo(1u);
        await Assert.That(PassiveBuffLevelRules.AbLevelFor(-7)).IsEqualTo(1u);
        await Assert.That(PassiveBuffLevelRules.AbLevelFor(55)).IsEqualTo(55u);
    }

    [Test]
    public async Task Apply_NpcPassive_KeepsTheAbLevelItHad()
    {
        // An NPC's passive list keeps AbLevel 1, so no NPC's numbers move with this change.
        var npc = new Npc { ObjId = 77, Level = 50 };
        var passive = new PassiveBuff
        {
            Id = AlwaysPassiveId, Template = SkillManager.Instance.GetPassiveBuffTemplate(AlwaysPassiveId)
        };

        passive.Apply(npc);

        var applied = npc.Buffs.GetEffectFromBuffId(AlwaysPassiveBuffId);
        await Assert.That(applied).IsNotNull();
        await Assert.That(applied.AbLevel).IsEqualTo(1u);
    }

    private static Character CreateCharacter(byte level, uint id = 1)
    {
        var character = new Character(new UnitCustomModelParams())
        {
            Id = id, ObjId = id, Name = $"Char{id}", Level = level
        };
        // Character.Skills is built by Character.Load, so a fresh one has to make it.
        character.Skills = new CharacterSkills(character);
        return character;
    }

    /// <summary>Enough levels for the point costs AddBuff checks; the content's own pool is not the subject.</summary>
    private static ExperienceManager CreateExperienceManager()
    {
        var manager = new ExperienceManager();
        var loader = Mock.Of<IExperienceLevelTemplateLoader>();
        loader.Load().Returns(Enumerable.Range(1, 60)
            .Select(level => new ExperienceLevelTemplate
            {
                Level = (byte)level,
                TotalExp = level * 100,
                TotalMateExp = level * 50,
                SkillPoints = level * 10
            })
            .ToArray());
        manager.Load(loader.Object, 60, 60);
        return manager;
    }

    private static void SetField(object target, string field, object value) =>
        target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
