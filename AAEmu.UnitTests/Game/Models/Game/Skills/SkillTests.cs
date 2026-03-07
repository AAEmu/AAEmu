using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_Default_CreatesSkillWithDefaultValues()
    {
        // Act
        var skill = new Skill();

        // Assert
        Assert.NotNull(skill);
        Assert.Equal(0u, skill.Id);
        Assert.Null(skill.Template);
        Assert.Equal(0, skill.Level);
        Assert.Equal(0, skill.TlId);
        Assert.NotNull(skill.HitTypes);
        Assert.Empty(skill.HitTypes);
    }

    [Fact]
    public void Constructor_WithTemplateAndOwner_CreatesSkillWithValues()
    {
        // Arrange
        var template = new SkillTemplate
        {
            Id = 100u,
            AbilityId = AbilityType.Fight,
            AbilityLevel = 1,
            LevelStep = 1
        };

        var mockOwner = new Mock<Unit>();
        mockOwner.Setup(o => o.GetAbLevel(AbilityType.Fight)).Returns(5);

        // Act
        var skill = new Skill(template, mockOwner.Object);

        // Assert
        Assert.NotNull(skill);
        Assert.Equal(100u, skill.Id);
        Assert.Equal(template, skill.Template);
        Assert.Equal(5, skill.Level); // (5 - 1) / 1 + 1 = 5
    }

    [Fact]
    public void Constructor_WithTemplateAndNoOwner_SetsLevelToOne()
    {
        // Arrange
        var template = new SkillTemplate
        {
            Id = 200u
        };

        // Act
        var skill = new Skill(template, null);

        // Assert
        Assert.Equal(200u, skill.Id);
        Assert.Equal(1, skill.Level);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void Id_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.Id = 123u;

        // Assert
        Assert.Equal(123u, skill.Id);
    }

    [Fact]
    public void Level_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.Level = 5;

        // Assert
        Assert.Equal(5, skill.Level);
    }

    [Fact]
    public void TlId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.TlId = 456;

        // Assert
        Assert.Equal(456, skill.TlId);
    }

    [Fact]
    public void CastTimeMultiplier_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.CastTimeMultiplier = 1.5f;

        // Assert
        Assert.Equal(1.5f, skill.CastTimeMultiplier);
    }

    #endregion

    #region HitTypes Tests

    [Fact]
    public void HitTypes_AddAndRetrieve_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.HitTypes[1] = SkillHitType.MeleeHit;

        // Assert
        Assert.Equal(SkillHitType.MeleeHit, skill.HitTypes[1]);
    }

    #endregion

    #region BypassGcd Tests

    // Note: _bypassGcd is private, so testing indirectly if possible

    #endregion

    #region Cancelled Tests

    [Fact]
    public void Cancelled_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.Cancelled = true;

        // Assert
        Assert.True(skill.Cancelled);
    }

    #endregion

    #region Callback Tests

    [Fact]
    public void Callback_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();
        var callbackCalled = false;
        Action callback = () => callbackCalled = true;

        // Act
        skill.Callback = callback;
        skill.Callback?.Invoke();

        // Assert
        Assert.True(callbackCalled);
    }

    #endregion

    #region Use Method Tests

    // Testing Use method requires extensive mocking, so basic tests

    [Fact]
    public void Use_WithNonUnitCaster_ReturnsInvalidSource()
    {
        // Arrange
        var skill = new Skill();
        var mockCaster = new Mock<BaseUnit>();
        var casterCaster = new SkillCasterUnit();
        var targetCaster = new SkillCastUnitTarget();

        // Act
        var result = skill.Use(mockCaster.Object, casterCaster, targetCaster, null, false, out var value);

        // Assert
        Assert.Equal(SkillResult.InvalidSource, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Constructor_WithNullTemplate_DoesNotThrow()
    {
        // Act
        var skill = new Skill(null, null);

        // Assert
        Assert.NotNull(skill);
        Assert.Equal(0u, skill.Id);
    }

    [Fact]
    public void Level_WithZeroAbilityLevel_CalculatesCorrectly()
    {
        // Arrange
        var template = new SkillTemplate
        {
            Id = 100u,
            AbilityId = AbilityType.Fight,
            AbilityLevel = 0,
            LevelStep = 2
        };

        var mockOwner = new Mock<Unit>();
        mockOwner.Setup(o => o.GetAbLevel(AbilityType.Fight)).Returns(10);

        // Act
        var skill = new Skill(template, mockOwner.Object);

        // Assert
        Assert.Equal(6, skill.Level); // (10 - 0) / 2 + 1 = 6
    }

    #endregion
}
