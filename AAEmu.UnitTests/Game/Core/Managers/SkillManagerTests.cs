using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SkillManagerTests
{
    #region Constructor Tests

    /// <summary>
    /// Verifies SkillManager can be constructed with injected deps.
    /// IAnimationManager and IPlotManager are only called during Load() which
    /// requires a SQLite DB — covered by integration tests.
    /// </summary>
    [Fact]
    public void Constructor_WithMockedDependencies_DoesNotThrow()
    {
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();

        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        Assert.NotNull(manager);
        mockAnimation.VerifyNoOtherCalls();
        mockPlot.VerifyNoOtherCalls();
    }

    #endregion

    #region GetSkillTemplate Tests

    [Fact]
    public void GetSkillTemplate_ReturnsSkill_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
    }

    #endregion

    #region GetBuffTemplate Tests

    [Fact]
    public void GetBuffTemplate_ReturnsBuff_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal(30000, result.Duration);
    }

    #endregion

    #region GetPassiveBuffTemplate Tests

    [Fact]
    public void GetPassiveBuffTemplate_ReturnsPassiveBuff_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal(100u, result.BuffId);
    }

    #endregion

    #region GetEffectTemplate Tests

    [Fact]
    public void GetEffectTemplate_ReturnsEffect_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void GetEffectTemplate_WithType_ReturnsEffect_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void GetEffectTemplate_WithType_ReturnsNull_ForNonExistentType()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var effectsField = typeof(SkillManager).GetField("_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var effects = new Dictionary<string, Dictionary<uint, EffectTemplate>>();
        effectsField?.SetValue(manager, effects);

        // Act
        var result = manager.GetEffectTemplate(1, "NonExistentEffect");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region IsDefaultSkill and IsCommonSkill Tests

    [Fact]
    public void IsDefaultSkill_ReturnsTrue_WhenSkillIsDefault()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.True(result);
    }

    [Fact]
    public void IsDefaultSkill_ReturnsFalse_WhenSkillIsNotDefault()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var defaultSkillsField = typeof(SkillManager).GetField("_defaultSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defaultSkills = new Dictionary<uint, DefaultSkill>();
        defaultSkillsField?.SetValue(manager, defaultSkills);

        // Act
        var result = manager.IsDefaultSkill(1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsCommonSkill_ReturnsTrue_WhenSkillIsCommon()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var commonSkillsField = typeof(SkillManager).GetField("_commonSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var commonSkills = new List<uint> { 1, 2, 3 };
        commonSkillsField?.SetValue(manager, commonSkills);

        // Act
        var result = manager.IsCommonSkill(1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsCommonSkill_ReturnsFalse_WhenSkillIsNotCommon()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var commonSkillsField = typeof(SkillManager).GetField("_commonSkills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var commonSkills = new List<uint> { 1, 2, 3 };
        commonSkillsField?.SetValue(manager, commonSkills);

        // Act
        var result = manager.IsCommonSkill(999);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetBuffTags and GetSkillTags Tests

    [Fact]
    public void GetBuffTags_ReturnsTags_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    #endregion

    #region GetDefaultSkills Tests

    [Fact]
    public void GetDefaultSkills_ReturnsSkills_WhenLoaded()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region GetModifiersByOwnerId Tests

    [Fact]
    public void GetModifiersByOwnerId_ReturnsModifiers_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Single(result);
    }

    #endregion

    #region GetCombatBuffs Tests

    [Fact]
    public void GetCombatBuffs_ReturnsBuffs_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Single(result);
    }

    #endregion

    #region GetSkillReagentsBySkillId Tests

    [Fact]
    public void GetSkillReagentsBySkillId_ReturnsReagents_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Single(result);
    }

    #endregion

    #region GetSkillProductsBySkillId Tests

    [Fact]
    public void GetSkillProductsBySkillId_ReturnsProducts_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Single(result);
    }

    #endregion

    #region GetBuffTriggerTemplates Tests

    [Fact]
    public void GetBuffTriggerTemplates_ReturnsTriggers_WhenExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Single(result);
    }

    #endregion

    #region GetSkillActAbility Tests

    [Fact]
    public void GetSkillActAbility_ReturnsNone_WhenSkillNotFound()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate>();
        skillsField?.SetValue(manager, skills);

        // Act
        var result = manager.GetSkillActAbility(1);

        // Assert
        Assert.Equal(ActabilityType.None, result);
    }

    [Fact]
    public void GetSkillActAbility_ReturnsActAbility_WhenSkillExists()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.Equal(ActabilityType.Alchemy, result);
    }

    #endregion

    #region GetNpSkillTemplate Tests

    [Fact]
    public void GetNpSkillTemplate_ReturnsNull_WhenSkillNotFound()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate>();
        skillsField?.SetValue(manager, skills);

        var npcSkill = new NpcSkill { SkillId = 1, SkillUseCondition = SkillUseConditionKind.None };

        // Act
        var result = manager.GetNpSkillTemplate(npcSkill);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetNpSkillTemplate_ReturnsSkill_WhenValid()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
    }

    #endregion

    #region GetSpawnGimmickEffect Tests

    [Fact]
    public void GetSpawnGimmickEffect_ReturnsEffect_WhenFound()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result);
        Assert.Equal(100u, result.GimmickId);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetSkillTemplate_HandlesMaxUInt32()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        var skillsField = typeof(SkillManager).GetField("_skills",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var skills = new Dictionary<uint, SkillTemplate>();
        skillsField?.SetValue(manager, skills);

        // Act
        var result = manager.GetSkillTemplate(uint.MaxValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void MultipleGetCalls_ReturnConsistentResults()
    {
        // Arrange
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();
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
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);
        Assert.Equal(result1.Id, result2.Id);
        Assert.Equal(result2.Id, result3.Id);
    }

    #endregion
}
