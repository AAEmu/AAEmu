using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffTests
{
    private static Mock<BaseUnit> CreateMockOwner()
    {
        var mock = new Mock<BaseUnit>();
        return mock;
    }

    private static Mock<BaseUnit> CreateMockCaster()
    {
        var mock = new Mock<BaseUnit>();
        return mock;
    }

    private static Buff CreateBuff(
        Mock<BaseUnit>? ownerMock = null,
        Mock<BaseUnit>? casterMock = null,    
        SkillCaster? skillCaster = null,
        BuffTemplate? template = null,
        Skill? skill = null,
        DateTime? startTime = null)
    {
        var mockOwner = ownerMock ?? CreateMockOwner();
        var mockCaster = casterMock ?? CreateMockCaster();
        var sc = skillCaster ?? new SkillCasterUnit(1);
        var tmpl = template ?? new BuffTemplate();
        var sk = skill ?? new Skill();
        var time = startTime ?? DateTime.UtcNow;

        return new Buff(mockOwner.Object, mockCaster.Object, sc, tmpl, sk, time);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        var mockOwner = CreateMockOwner();
        var mockCaster = CreateMockCaster();
        var skillCaster = new SkillCasterUnit(1);
        var template = new BuffTemplate();
        var skill = new Skill();
        var startTime = DateTime.UtcNow;

        // Act
        var buff = new Buff(mockOwner.Object, mockCaster.Object, skillCaster, template, skill, startTime);

        // Assert
        Assert.NotNull(buff);
        Assert.NotNull(buff.Owner);
        Assert.Equal(skillCaster, buff.SkillCaster);
        Assert.Equal(template, buff.Template);
        Assert.Equal(skill, buff.Skill);
        Assert.Equal(startTime, buff.StartTime);
        Assert.Equal(DateTime.MinValue, buff.EndTime);
        Assert.Equal(1u, buff.AbLevel);
        Assert.Equal(EffectState.Created, buff.State);
        Assert.False(buff.InUse);
        Assert.Equal(0, buff.Duration);
        Assert.Equal(0.0, buff.Tick);
        Assert.Equal(0, buff.Charge);
        Assert.False(buff.Passive);
        Assert.NotNull(buff.Events);
        Assert.NotNull(buff.Triggers);
        Assert.NotNull(buff.saveFactions);
    }

    [Fact]
    public void Constructor_WithNullParameters_DoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => new Buff(null!, null!, null!, null!, null!, DateTime.UtcNow));

        // Assert - constructor should not throw even with nulls
        Assert.Null(exception);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void Index_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Index = 5u;

        // Assert
        Assert.Equal(5u, buff.Index);
    }

    [Fact]
    public void State_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.State = EffectState.Acting;

        // Assert
        Assert.Equal(EffectState.Acting, buff.State);
    }

    [Fact]
    public void InUse_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.InUse = true;

        // Assert
        Assert.True(buff.InUse);
    }

    [Fact]
    public void Duration_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Duration = 10000;

        // Assert
        Assert.Equal(10000, buff.Duration);
    }

    [Fact]
    public void Tick_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Tick = 500.5;

        // Assert
        Assert.Equal(500.5, buff.Tick);
    }

    [Fact]
    public void Charge_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Charge = 3;

        // Assert
        Assert.Equal(3, buff.Charge);
    }

    [Fact]
    public void Passive_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Passive = true;

        // Assert
        Assert.True(buff.Passive);
    }

    [Fact]
    public void AbLevel_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.AbLevel = 10u;

        // Assert
        Assert.Equal(10u, buff.AbLevel);
    }

    #endregion

    #region IsEnded Tests

    [Theory]
    [InlineData(EffectState.Finished, true)]
    [InlineData(EffectState.Finishing, true)]
    [InlineData(EffectState.Created, false)]
    [InlineData(EffectState.Acting, false)]
    public void IsEnded_ReturnsCorrectValue(EffectState state, bool expected)
    {
        // Arrange
        var buff = CreateBuff();
        buff.State = state;

        // Act
        var result = buff.IsEnded();

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region GetTimeLeft Tests

    [Fact]
    public void GetTimeLeft_WithZeroDuration_ReturnsNegativeOne()
    {
        // Arrange
        var buff = CreateBuff();
        buff.Duration = 0;

        // Act
        var result = buff.GetTimeLeft();

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public void GetTimeLeft_WithPositiveDuration_ReturnsTimeLeft()
    {
        // Arrange
        var buff = CreateBuff();
        buff.Duration = 10000;
        buff.StartTime = DateTime.UtcNow.AddSeconds(-5); // Started 5 seconds ago

        // Act
        var result = buff.GetTimeLeft();

        // Assert
        Assert.True(result >= 0);
        Assert.True(result <= 5000); // Should be around 5 seconds left
    }

    [Fact]
    public void GetTimeLeft_WithExpiredDuration_ReturnsZero()
    {
        // Arrange
        var buff = CreateBuff();
        buff.Duration = 1000;
        buff.StartTime = DateTime.UtcNow.AddSeconds(-10); // Started 10 seconds ago, duration was 1 second

        // Act
        var result = buff.GetTimeLeft();

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region GetTimeElapsed Tests

    [Fact]
    public void GetTimeElapsed_ReturnsPositiveValue()
    {
        // Arrange
        var buff = CreateBuff();
        buff.StartTime = DateTime.UtcNow.AddSeconds(-3);

        // Act
        var result = buff.GetTimeElapsed();

        // Assert
        Assert.True(result >= 3000);
    }

    [Fact]
    public void GetTimeElapsed_AtStartTime_ReturnsZero()
    {
        // Arrange
        var buff = CreateBuff();
        buff.StartTime = DateTime.UtcNow;

        // Act
        var result = buff.GetTimeElapsed();

        // Assert
        Assert.True(result >= 0);
    }

    #endregion

    #region WriteData Tests

    [Fact]
    public void WriteData_WritesCorrectData()
    {
        // Arrange
        var buff = CreateBuff();
        buff.Charge = 5;
        buff.Duration = 10000;
        buff.Tick = 1000;

        var stream = new PacketStream();

        // Act
        buff.WriteData(stream);

        // Assert
        Assert.NotNull(stream);
    }

    #endregion

    #region ConsumeCharge Tests

    [Theory]
    [InlineData(10, 5, 5, 0)]  // charge=10, consume=5 -> new charge=5, returned=0
    [InlineData(3, 5, 0, 2)]  // charge=3, consume=5 -> new charge=0, returned=2
    [InlineData(0, 5, 0, 5)]  // charge=0, consume=5 -> new charge=0, returned=5
    [InlineData(5, 5, 0, 0)]  // charge=5, consume=5 -> new charge=0, returned=0
    public void ConsumeCharge_ReturnsCorrectValue(int initialCharge, int consumeValue, int expectedCharge, int expectedReturned)
    {
        // Arrange
        var buff = CreateBuff();
        buff.Charge = initialCharge;

        // Act
        var result = buff.ConsumeCharge(consumeValue);

        // Assert
        Assert.Equal(expectedCharge, buff.Charge);
        Assert.Equal(expectedReturned, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SaveFactions_InitializeAsEmpty()
    {
        // Arrange & Act
        var buff = CreateBuff();

        // Assert
        Assert.NotNull(buff.saveFactions);
        Assert.Empty(buff.saveFactions);
    }

    [Fact]
    public void Skill_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();
        var skill = new Skill();

        // Act
        buff.Skill = skill;

        // Assert
        Assert.Equal(skill, buff.Skill);
    }

    [Fact]
    public void Template_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();
        var template = new BuffTemplate();

        // Act
        buff.Template = template;

        // Assert
        Assert.Equal(template, buff.Template);
    }

    [Fact]
    public void SkillCaster_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();
        var skillCaster = new SkillCasterUnit(42);

        // Act
        buff.SkillCaster = skillCaster;

        // Assert
        Assert.Equal(skillCaster, buff.SkillCaster);
    }

    #endregion
}
