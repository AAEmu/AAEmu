using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using Moq;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_Default_CreatesSkillWithDefaultValues()
    {
        // Act
        var skill = new Skill();

        // Assert
        await Assert.That(skill).IsNotNull();
        await Assert.That(skill.Id).IsEqualTo(0u);
        await Assert.That(skill.Template).IsNull();
        await Assert.That(skill.Level).IsEqualTo((byte)0);
        await Assert.That(skill.TlId).IsEqualTo((byte)0);
        await Assert.That(skill.HitTypes).IsNotNull();
        await Assert.That(skill.HitTypes).IsEmpty();
    }

    [Test]
    public async Task Constructor_WithTemplateAndOwner_CreatesSkillWithValues()
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
        await Assert.That(skill).IsNotNull();
        await Assert.That(skill.Id).IsEqualTo(100u);
        await Assert.That(skill.Template).IsEqualTo(template);
        await Assert.That(skill.Level).IsEqualTo((byte)5); // (5 - 1) / 1 + 1 = 5
    }

    [Test]
    public async Task Constructor_WithTemplateAndNoOwner_SetsLevelToOne()
    {
        // Arrange
        var template = new SkillTemplate
        {
            Id = 200u
        };

        // Act
        var skill = new Skill(template, null);

        // Assert
        await Assert.That(skill.Id).IsEqualTo(200u);
        await Assert.That(skill.Level).IsEqualTo((byte)1);
    }

    #endregion

    #region Property Tests

    [Test]
    public async Task Id_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.Id = 123u;

        // Assert
        await Assert.That(skill.Id).IsEqualTo(123u);
    }

    [Test]
    public async Task Level_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.Level = 5;

        // Assert
        await Assert.That(skill.Level).IsEqualTo((byte)5);
    }

    [Test]
    public async Task TlId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.TlId = 456;

        // Assert
        await Assert.That(skill.TlId).IsEqualTo((ushort)456);
    }

    [Test]
    public async Task CastTimeMultiplier_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.CastTimeMultiplier = 1.5f;

        // Assert
        await Assert.That(skill.CastTimeMultiplier).IsEqualTo(1.5f);
    }

    #endregion

    #region HitTypes Tests

    [Test]
    public async Task HitTypes_AddAndRetrieve_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.HitTypes[1] = SkillHitType.MeleeHit;

        // Assert
        await Assert.That(skill.HitTypes[1]).IsEqualTo(SkillHitType.MeleeHit);
    }

    #endregion

    #region BypassGcd Tests

    // Note: _bypassGcd is private, so testing indirectly if possible

    #endregion

    #region Cancelled Tests

    [Test]
    public async Task Cancelled_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();

        // Act
        skill.Cancelled = true;

        // Assert
        await Assert.That(skill.Cancelled).IsTrue();
    }

    #endregion

    #region Callback Tests

    [Test]
    public async Task Callback_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var skill = new Skill();
        var callbackCalled = false;
        Action callback = () => callbackCalled = true;

        // Act
        skill.Callback = callback;
        skill.Callback?.Invoke();

        // Assert
        await Assert.That(callbackCalled).IsTrue();
    }

    #endregion

    #region Use Method Tests

    // Testing Use method requires extensive mocking, so basic tests

    [Test]
    public async Task Use_WithNonUnitCaster_ReturnsInvalidSource()
    {
        // Arrange
        var skill = new Skill();
        var mockCaster = new Mock<BaseUnit>();
        var casterCaster = new SkillCasterUnit();
        var targetCaster = new SkillCastUnitTarget();

        // Act
        var result = skill.Use(mockCaster.Object, casterCaster, targetCaster, null, false, out var value);

        // Assert
        await Assert.That(result).IsEqualTo(SkillResult.InvalidSource);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Constructor_WithNullTemplate_DoesNotThrow()
    {
        // Act
        var skill = new Skill(null, null);

        // Assert
        await Assert.That(skill).IsNotNull();
        await Assert.That(skill.Id).IsEqualTo(0u);
    }

    [Test]
    public async Task Level_WithZeroAbilityLevel_CalculatesCorrectly()
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
        await Assert.That(skill.Level).IsEqualTo((byte)6); // (10 - 0) / 2 + 1 = 6
    }

    #endregion
}
