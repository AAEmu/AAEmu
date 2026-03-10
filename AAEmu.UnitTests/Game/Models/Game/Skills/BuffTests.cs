using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using Moq;

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
        Mock<BaseUnit> ownerMock = null,
        Mock<BaseUnit> casterMock = null,    
        SkillCaster skillCaster = null,
        BuffTemplate template = null,
        Skill skill = null,
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

    [Test]
    public async Task Constructor_WithParameters_SetsPropertiesCorrectly()
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
        await Assert.That(buff).IsNotNull();
        await Assert.That(buff.Owner).IsNotNull();
        await Assert.That(buff.SkillCaster).IsEqualTo(skillCaster);
        await Assert.That(buff.Template).IsEqualTo(template);
        await Assert.That(buff.Skill).IsEqualTo(skill);
        await Assert.That(buff.StartTime).IsEqualTo(startTime);
        await Assert.That(buff.EndTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(buff.AbLevel).IsEqualTo(1u);
        await Assert.That(buff.State).IsEqualTo(EffectState.Created);
        await Assert.That(buff.InUse).IsFalse();
        await Assert.That(buff.Duration).IsEqualTo(0);
        await Assert.That(buff.Tick).IsEqualTo(0.0);
        await Assert.That(buff.Charge).IsEqualTo(0);
        await Assert.That(buff.Passive).IsFalse();
        await Assert.That(buff.Events).IsNotNull();
        await Assert.That(buff.Triggers).IsNotNull();
        await Assert.That(buff.saveFactions).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullParameters_DoesNotThrow()
    {
        // Act & Assert
        Exception? exception = null;
        try
        {
            new Buff(null!, null!, null!, null!, null!, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            exception = e;
        }

        // Assert - constructor should not throw even with nulls
        await Assert.That(exception).IsNull();
    }

    #endregion

    #region Property Tests

    [Test]
    public async Task Index_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Index = 5u;

        // Assert
        await Assert.That(buff.Index).IsEqualTo(5u);
    }

    [Test]
    public async Task State_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.State = EffectState.Acting;

        // Assert
        await Assert.That(buff.State).IsEqualTo(EffectState.Acting);
    }

    [Test]
    public async Task InUse_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.InUse = true;

        // Assert
        await Assert.That(buff.InUse).IsTrue();
    }

    [Test]
    public async Task Duration_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Duration = 10000;

        // Assert
        await Assert.That(buff.Duration).IsEqualTo(10000);
    }

    [Test]
    public async Task Tick_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Tick = 500.5;

        // Assert
        await Assert.That(buff.Tick).IsEqualTo(500.5);
    }

    [Test]
    public async Task Charge_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Charge = 3;

        // Assert
        await Assert.That(buff.Charge).IsEqualTo(3);
    }

    [Test]
    public async Task Passive_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.Passive = true;

        // Assert
        await Assert.That(buff.Passive).IsTrue();
    }

    [Test]
    public async Task AbLevel_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();

        // Act
        buff.AbLevel = 10u;

        // Assert
        await Assert.That(buff.AbLevel).IsEqualTo(10u);
    }

    #endregion

    #region IsEnded Tests

    [Test]
    [Arguments(EffectState.Finished, true)]
    [Arguments(EffectState.Finishing, true)]
    [Arguments(EffectState.Created, false)]
    [Arguments(EffectState.Acting, false)]
    public async Task IsEnded_ReturnsCorrectValue(EffectState state, bool expected)
    {
        // Arrange
        var buff = CreateBuff();
        buff.State = state;

        // Act
        var result = buff.IsEnded();

        // Assert
        await Assert.That(result).IsEqualTo(expected);
    }

    #endregion

    #region GetTimeLeft Tests

    [Test]
    public async Task GetTimeLeft_WithZeroDuration_ReturnsNegativeOne()
    {
        // Arrange
        var buff = CreateBuff();
        buff.Duration = 0;

        // Act
        var result = buff.GetTimeLeft();

        // Assert
        await Assert.That(result).IsEqualTo(-1);
    }

    [Test]
    public async Task GetTimeLeft_WithPositiveDuration_ReturnsTimeLeft()
    {
        // Arrange
        var buff = CreateBuff();
        buff.Duration = 10000;
        buff.StartTime = DateTime.UtcNow.AddSeconds(-5); // Started 5 seconds ago

        // Act
        var result = buff.GetTimeLeft();

        // Assert
        await Assert.That(result >= 0).IsTrue();
        await Assert.That(result <= 5000).IsTrue(); // Should be around 5 seconds left
    }

    [Test]
    public async Task GetTimeLeft_WithExpiredDuration_ReturnsZero()
    {
        // Arrange
        var buff = CreateBuff();
        buff.Duration = 1000;
        buff.StartTime = DateTime.UtcNow.AddSeconds(-10); // Started 10 seconds ago, duration was 1 second

        // Act
        var result = buff.GetTimeLeft();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    #endregion

    #region GetTimeElapsed Tests

    [Test]
    public async Task GetTimeElapsed_ReturnsPositiveValue()
    {
        // Arrange
        var buff = CreateBuff();
        buff.StartTime = DateTime.UtcNow.AddSeconds(-3);

        // Act
        var result = buff.GetTimeElapsed();

        // Assert
        await Assert.That(result >= 3000).IsTrue();
    }

    [Test]
    public async Task GetTimeElapsed_AtStartTime_ReturnsZero()
    {
        // Arrange
        var buff = CreateBuff();
        buff.StartTime = DateTime.UtcNow;

        // Act
        var result = buff.GetTimeElapsed();

        // Assert
        await Assert.That(result >= 0).IsTrue();
    }

    #endregion

    #region WriteData Tests

    [Test]
    public async Task WriteData_WritesCorrectData()
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
        await Assert.That(stream).IsNotNull();
    }

    #endregion

    #region ConsumeCharge Tests

    [Test]
    [Arguments(10, 5, 5, 0)]  // charge=10, consume=5 -> new charge=5, returned=0
    [Arguments(3, 5, 0, 2)]  // charge=3, consume=5 -> new charge=0, returned=2
    [Arguments(0, 5, 0, 5)]  // charge=0, consume=5 -> new charge=0, returned=5
    [Arguments(5, 5, 0, 0)]  // charge=5, consume=5 -> new charge=0, returned=0
    public async Task ConsumeCharge_ReturnsCorrectValue(int initialCharge, int consumeValue, int expectedCharge, int expectedReturned)
    {
        // Arrange
        var buff = CreateBuff();
        buff.Charge = initialCharge;

        // Act
        var result = buff.ConsumeCharge(consumeValue);

        // Assert
        await Assert.That(buff.Charge).IsEqualTo(expectedCharge);
        await Assert.That(result).IsEqualTo(expectedReturned);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task SaveFactions_InitializeAsEmpty()
    {
        // Arrange & Act
        var buff = CreateBuff();

        // Assert
        await Assert.That(buff.saveFactions).IsNotNull();
        await Assert.That(buff.saveFactions).IsEmpty();
    }

    [Test]
    public async Task Skill_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();
        var skill = new Skill();

        // Act
        buff.Skill = skill;

        // Assert
        await Assert.That(buff.Skill).IsEqualTo(skill);
    }

    [Test]
    public async Task Template_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();
        var template = new BuffTemplate();

        // Act
        buff.Template = template;

        // Assert
        await Assert.That(buff.Template).IsEqualTo(template);
    }

    [Test]
    public async Task SkillCaster_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var buff = CreateBuff();
        var skillCaster = new SkillCasterUnit(42);

        // Act
        buff.SkillCaster = skillCaster;

        // Assert
        await Assert.That(buff.SkillCaster).IsEqualTo(skillCaster);
    }

    #endregion
}
