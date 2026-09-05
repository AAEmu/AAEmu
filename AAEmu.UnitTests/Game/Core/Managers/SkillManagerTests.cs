using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SkillManagerTests
{
    #region Constructor Tests

    /// <summary>
    /// Verifies SkillManager can be constructed with injected deps.
    /// IAnimationManager and IPlotManager are only called during Load() which
    /// requires a SQLite DB — covered by integration tests.
    /// </summary>
    [Test]
    public async Task Constructor_WithMockedDependencies_DoesNotThrow()
    {
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();

        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockAnimation);
        Mock.VerifyNoOtherCalls(mockPlot);
    }

    #endregion

    #region GetSkillTemplate Tests

    [Test]
    public async Task GetSkillTemplate_ReturnsSkill_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testSkill = new SkillTemplate
        {
            Id = 1,
            CooldownTime = 5000,
            CastingTime = 1000
        };

        // Use reflection to set the private _skills field
        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate> { { 1, testSkill } };
        skillsField?.SetValue(manager, skills);

        // Act
        var result = manager.GetSkillTemplate(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
    }

    #endregion

    #region GetBuffTemplate Tests

    [Test]
    public async Task GetBuffTemplate_ReturnsBuff_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testBuff = new BuffTemplate
        {
            Id = 1,
            Duration = 30000
        };

        var buffsField = typeof(SkillManager).GetField("_buffs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var buffs = new Dictionary<uint, BuffTemplate> { { 1, testBuff } };
        buffsField?.SetValue(manager, buffs);

        // Act
        var result = manager.GetBuffTemplate(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.Duration).IsEqualTo(30000);
    }

    #endregion

    #region GetPassiveBuffTemplate Tests

    [Test]
    public async Task GetPassiveBuffTemplate_ReturnsPassiveBuff_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testPassiveBuff = new PassiveBuffTemplate
        {
            Id = 1,
            BuffId = 100,
            Level = 5
        };

        var passiveBuffsField = typeof(SkillManager).GetField("_passiveBuffs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var passiveBuffs = new Dictionary<uint, PassiveBuffTemplate> { { 1, testPassiveBuff } };
        passiveBuffsField?.SetValue(manager, passiveBuffs);

        // Act
        var result = manager.GetPassiveBuffTemplate(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.BuffId).IsEqualTo(100u);
    }

    #endregion

    #region GetEffectTemplate Tests

    [Test]
    public async Task GetEffectTemplate_ReturnsEffect_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testEffect = new DamageEffect { Id = 1 };

        // Set up _types and _effects fields
        var typesField = typeof(SkillManager).GetField("_types",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var types = new Dictionary<uint, EffectType>
        {
            { 1, new EffectType { Id = 1, ActualId = 1, Type = "DamageEffect" } }
        };
        typesField?.SetValue(manager, types);

        var effectsField = typeof(SkillManager).GetField("_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var effects = new Dictionary<string, Dictionary<uint, EffectTemplate>>
        {
            { "DamageEffect", new Dictionary<uint, EffectTemplate> { { 1, testEffect } } }
        };
        effectsField?.SetValue(manager, effects);

        // Act
        var result = manager.GetEffectTemplate(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task GetEffectTemplate_WithType_ReturnsEffect_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testEffect = new HealEffect { Id = 1 };

        var effectsField = typeof(SkillManager).GetField("_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var effects = new Dictionary<string, Dictionary<uint, EffectTemplate>>
        {
            { "HealEffect", new Dictionary<uint, EffectTemplate> { { 1, testEffect } } }
        };
        effectsField?.SetValue(manager, effects);

        // Act
        var result = manager.GetEffectTemplate(1, "HealEffect");

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task GetEffectTemplate_WithType_ReturnsNull_ForNonExistentType()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var effectsField = typeof(SkillManager).GetField("_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var effects = new Dictionary<string, Dictionary<uint, EffectTemplate>>();
        effectsField?.SetValue(manager, effects);

        // Act
        var result = manager.GetEffectTemplate(1, "NonExistentEffect");

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region IsDefaultSkill and IsCommonSkill Tests

    [Test]
    public async Task IsDefaultSkill_ReturnsTrue_WhenSkillIsDefault()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var defaultSkillsField = typeof(SkillManager).GetField("_defaultSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defaultSkills = new Dictionary<uint, DefaultSkill>
        {
            { 1, new DefaultSkill { Template = new SkillTemplate { Id = 1 } } }
        };
        defaultSkillsField?.SetValue(manager, defaultSkills);

        // Act
        var result = manager.IsDefaultSkill(1);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsDefaultSkill_ReturnsFalse_WhenSkillIsNotDefault()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var defaultSkillsField = typeof(SkillManager).GetField("_defaultSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defaultSkills = new Dictionary<uint, DefaultSkill>();
        defaultSkillsField?.SetValue(manager, defaultSkills);

        // Act
        var result = manager.IsDefaultSkill(1);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsCommonSkill_ReturnsTrue_WhenSkillIsCommon()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var commonSkillsField = typeof(SkillManager).GetField("_commonSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var commonSkills = new List<uint> { 1, 2, 3 };
        commonSkillsField?.SetValue(manager, commonSkills);

        // Act
        var result = manager.IsCommonSkill(1);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsCommonSkill_ReturnsFalse_WhenSkillIsNotCommon()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var commonSkillsField = typeof(SkillManager).GetField("_commonSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var commonSkills = new List<uint> { 1, 2, 3 };
        commonSkillsField?.SetValue(manager, commonSkills);

        // Act
        var result = manager.IsCommonSkill(999);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region GetBuffTags and GetSkillTags Tests

    [Test]
    public async Task GetBuffTags_ReturnsTags_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var buffTagsField = typeof(SkillManager).GetField("_buffTags",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var buffTags = new Dictionary<uint, List<uint>>
        {
            { 1, new List<uint> { 10, 20, 30 } }
        };
        buffTagsField?.SetValue(manager, buffTags);

        // Act
        var result = manager.GetBuffTags(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(3);
    }

    #endregion

    #region GetDefaultSkills Tests

    [Test]
    public async Task GetDefaultSkills_ReturnsSkills_WhenLoaded()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var defaultSkillsField = typeof(SkillManager).GetField("_defaultSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defaultSkills = new Dictionary<uint, DefaultSkill>
        {
            { 1, new DefaultSkill { Template = new SkillTemplate { Id = 1 }, Slot = 0 } },
            { 2, new DefaultSkill { Template = new SkillTemplate { Id = 2 }, Slot = 1 } }
        };
        defaultSkillsField?.SetValue(manager, defaultSkills);

        // Act
        var result = manager.GetDefaultSkills();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetDefaultSkills_ForRace_DropsTheOtherRacesRacial()
    {
        var manager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(SkillManager).GetField("_defaultSkills", flags)?.SetValue(manager, new Dictionary<uint, DefaultSkill>
        {
            { 2, new DefaultSkill { Template = new SkillTemplate { Id = 2 }, Slot = 13, AddToSlot = true } },
            { 35420, new DefaultSkill { Template = new SkillTemplate { Id = 35420 }, Slot = 17, AddToSlot = true } },
            { 35423, new DefaultSkill { Template = new SkillTemplate { Id = 35423 }, Slot = 17, AddToSlot = true } }
        });
        typeof(SkillManager).GetField("_raceAssignedDefaultSkillIds", flags)
            ?.SetValue(manager, new HashSet<uint> { 35420, 35423 });
        typeof(SkillManager).GetField("_defaultSkillIdsByRaceGender", flags)?.SetValue(manager,
            new Dictionary<(byte Race, byte Gender), HashSet<uint>>
            {
                { ((byte)Race.Nuian, (byte)Gender.Male), [35420] },
                { ((byte)Race.Hariharan, (byte)Gender.Male), [35423] }
            });

        var nuian = manager.GetDefaultSkills(Race.Nuian, Gender.Male);
        var hariharan = manager.GetDefaultSkills(Race.Hariharan, Gender.Male);

        await Assert.That(nuian.Select(s => s.Template.Id).ToHashSet().SetEquals([2u, 35420u])).IsTrue();
        await Assert.That(hariharan.Select(s => s.Template.Id).ToHashSet().SetEquals([2u, 35423u])).IsTrue();
        await Assert.That(manager.IsDefaultSkill(35420, Race.Nuian, Gender.Male)).IsTrue();
        await Assert.That(manager.IsDefaultSkill(35423, Race.Nuian, Gender.Male)).IsFalse();
        await Assert.That(manager.IsDefaultSkill(2, Race.Nuian, Gender.Male)).IsTrue();
    }

    #endregion

    #region GetModifiersByOwnerId Tests

    [Test]
    public async Task GetModifiersByOwnerId_ReturnsModifiers_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var modifiersField = typeof(SkillManager).GetField("_skillModifiers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var modifiers = new Dictionary<uint, List<SkillModifier>>
        {
            { 1, new List<SkillModifier> { new SkillModifier { Id = 1, OwnerId = 1 } } }
        };
        modifiersField?.SetValue(manager, modifiers);

        // Act
        var result = manager.GetModifiersByOwnerId(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).HasSingleItem();
    }

    #endregion

    #region GetCombatBuffs Tests

    [Test]
    public async Task GetCombatBuffs_ReturnsBuffs_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var combatBuffsField = typeof(SkillManager).GetField("_combatBuffs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var combatBuffs = new Dictionary<uint, List<CombatBuffTemplate>>
        {
            { 1, new List<CombatBuffTemplate> { new CombatBuffTemplate { Id = 1, BuffId = 100 } } }
        };
        combatBuffsField?.SetValue(manager, combatBuffs);

        // Act
        var result = manager.GetCombatBuffs(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).HasSingleItem();
    }

    #endregion

    #region GetSkillReagentsBySkillId Tests

    [Test]
    public async Task GetSkillReagentsBySkillId_ReturnsReagents_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var reagentsField = typeof(SkillManager).GetField("_skillReagents",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var reagents = new Dictionary<uint, SkillReagent>
        {
            { 1, new SkillReagent { Id = 1, SkillId = 1, ItemId = 100, Amount = 5 } }
        };
        reagentsField?.SetValue(manager, reagents);

        // Act
        var result = manager.GetSkillReagentsBySkillId(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).HasSingleItem();
    }

    #endregion

    #region GetSkillProductsBySkillId Tests

    [Test]
    public async Task GetSkillProductsBySkillId_ReturnsProducts_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var productsField = typeof(SkillManager).GetField("_skillProducts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var products = new Dictionary<uint, SkillProduct>
        {
            { 1, new SkillProduct { Id = 1, SkillId = 1, ItemId = 200, Amount = 1 } }
        };
        productsField?.SetValue(manager, products);

        // Act
        var result = manager.GetSkillProductsBySkillId(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).HasSingleItem();
    }

    #endregion

    #region GetBuffTriggerTemplates Tests

    [Test]
    public async Task GetBuffTriggerTemplates_ReturnsTriggers_WhenExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var buffTriggersField = typeof(SkillManager).GetField("_buffTriggers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var buffTriggers = new Dictionary<uint, List<BuffTriggerTemplate>>
        {
            { 1, new List<BuffTriggerTemplate> { new BuffTriggerTemplate { Id = 1 } } }
        };
        buffTriggersField?.SetValue(manager, buffTriggers);

        // Act
        var result = manager.GetBuffTriggerTemplates(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).HasSingleItem();
    }

    #endregion

    #region GetSkillActAbility Tests

    [Test]
    public async Task GetSkillActAbility_ReturnsNone_WhenSkillNotFound()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate>();
        skillsField?.SetValue(manager, skills);

        // Act
        var result = manager.GetSkillActAbility(1);

        // Assert
        await Assert.That(result).IsEqualTo(ActabilityType.None);
    }

    [Test]
    public async Task GetSkillActAbility_ReturnsActAbility_WhenSkillExists()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testSkill = new SkillTemplate
        {
            Id = 1,
            ActabilityGroupId = (int)ActabilityType.Alchemy
        };

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate> { { 1, testSkill } };
        skillsField?.SetValue(manager, skills);

        // Act
        var result = manager.GetSkillActAbility(1);

        // Assert
        await Assert.That(result).IsEqualTo(ActabilityType.Alchemy);
    }

    #endregion

    #region GetNpSkillTemplate Tests

    [Test]
    public async Task GetNpSkillTemplate_ReturnsNull_WhenSkillNotFound()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate>();
        skillsField?.SetValue(manager, skills);

        var npcSkill = new NpcSkill { SkillId = 1, SkillUseCondition = SkillUseConditionKind.None };

        // Act
        var result = manager.GetNpSkillTemplate(npcSkill);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetNpSkillTemplate_ReturnsSkill_WhenValid()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testSkill = new SkillTemplate
        {
            Id = 1,
            IgnoreGlobalCooldown = true
        };

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate> { { 1, testSkill } };
        skillsField?.SetValue(manager, skills);

        var npcSkill = new NpcSkill { SkillId = 1, SkillUseCondition = SkillUseConditionKind.None };

        // Act
        var result = manager.GetNpSkillTemplate(npcSkill);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
    }

    #endregion

    #region GetSpawnGimmickEffect Tests

    [Test]
    public async Task GetSpawnGimmickEffect_ReturnsEffect_WhenFound()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testEffect = new SpawnGimmickEffect { Id = 1, GimmickId = 100 };

        var effectsField = typeof(SkillManager).GetField("_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var effects = new Dictionary<string, Dictionary<uint, EffectTemplate>>
        {
            { "SpawnGimmickEffect", new Dictionary<uint, EffectTemplate> { { 1, testEffect } } }
        };
        effectsField?.SetValue(manager, effects);

        // Act
        var result = manager.GetSpawnGimmickEffect(100);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.GimmickId).IsEqualTo(100u);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task GetSkillTemplate_HandlesMaxUInt32()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate>();
        skillsField?.SetValue(manager, skills);

        // Act
        var result = manager.GetSkillTemplate(uint.MaxValue);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MultipleGetCalls_ReturnConsistentResults()
    {
        // Arrange
        var mockAnimation = Mock.Of<IAnimationManager>();
        var mockPlot = Mock.Of<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var testSkill = new SkillTemplate { Id = 1, CooldownTime = 5000 };

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate> { { 1, testSkill } };
        skillsField?.SetValue(manager, skills);

        // Act - Call multiple times
        var result1 = manager.GetSkillTemplate(1);
        var result2 = manager.GetSkillTemplate(1);
        var result3 = manager.GetSkillTemplate(1);

        // Assert
        await Assert.That(result1).IsNotNull();
        await Assert.That(result2).IsNotNull();
        await Assert.That(result3).IsNotNull();
        await Assert.That(result2.Id).IsEqualTo(result1.Id);
        await Assert.That(result3.Id).IsEqualTo(result2.Id);
    }

    #endregion
}