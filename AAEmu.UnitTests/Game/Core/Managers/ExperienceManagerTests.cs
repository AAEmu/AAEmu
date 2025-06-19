using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;

using Moq;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ExperienceManagerTests
{
    private readonly ExperienceManager _cut = ExperienceManager.Instance;
    private const int maxPlayerExp = 200;
    private const int maxMateExp = 100;

    public ExperienceManagerTests()
    {
        SetupExperienceManager([
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0, SkillPoints = 1 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100, TotalMateExp = 50, SkillPoints = 2 },
            new ExperienceLevelTemplate { Level = 3, TotalExp = 200, TotalMateExp = 100, SkillPoints = 3 }
        ]);
    }

    [Fact]
    public void MaxPlayerLevelGreaterThanZero()
    {
        Assert.True(_cut.MaxPlayerLevel > 0);
    }

    [Fact]
    public void MaxMateLevelGreaterThanZero()
    {
        Assert.True(_cut.MaxMateLevel > 0);
    }

    [Theory]
    // Character
    [InlineData(0, false, 0)] // Level 0 is invalid for character, so should return 0 exp
    [InlineData(1, false, 0)]
    [InlineData(2, false, 100)]
    [InlineData(3, false, 200)]
    [InlineData(4, false, 0)] // Level 3 is invalid for character, so should return 0 exp
    // Mate
    [InlineData(0, true, 0)] // Level 0 is invalid for mate, so should return 0 exp
    [InlineData(1, true, 0)]
    [InlineData(2, true, 50)]
    [InlineData(3, true, 100)]
    [InlineData(4, true, 0)] // Level 3 is invalid for mate, so should return 0 exp
    public void GetExpForLevel(byte level, bool mate, int expectedExp)
    {
        var exp = _cut.GetExpForLevel(level, mate);
        Assert.Equal(expectedExp, exp);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void GetLevelFromExp_Invalid_TooSmall_Throws(int exp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetLevelFromExp(exp, out _));
    }

    [Theory]
    // Characters
    [InlineData(201, false)]
    [InlineData(300, false)]
    [InlineData(int.MaxValue, false)]
    // Mates
    [InlineData(101, true)]
    [InlineData(300, true)]
    [InlineData(int.MaxValue, true)]
    public void GetLevelFromExp_Invalid_TooBig_ReturnsMaxLevel(int exp, bool mate)
    {
        var level = _cut.GetLevelFromExp(exp, out var overflow, mate);

        var expectedLevel = mate ? _cut.MaxMateLevel : _cut.MaxPlayerLevel;
        var expectedOverflow = exp - (mate ? maxMateExp : maxPlayerExp);

        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedOverflow, overflow);
    }

    [Theory]
    // Character
    [InlineData(0, false, 1, 0)]
    [InlineData(1, false, 1, 1)]
    [InlineData(99, false, 1, 99)]
    [InlineData(100, false, 2, 0)]
    [InlineData(101, false, 2, 1)]
    [InlineData(199, false, 2, 99)]
    [InlineData(200, false, 3, 0)]
    // Mate
    [InlineData(0, true, 1, 0)]
    [InlineData(1, true, 1, 1)]
    [InlineData(49, true, 1, 49)]
    [InlineData(50, true, 2, 0)]
    [InlineData(51, true, 2, 1)]
    [InlineData(99, true, 2, 49)]
    [InlineData(100, true, 3, 0)]
    public void GetLevelFromExp_Valid(int exp, bool mate, int expectedLevel, int expectedOverflow)
    {
        var level = _cut.GetLevelFromExp(exp, out var overflow, mate);
        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedOverflow, overflow);
    }

    [Fact]
    public void GetLevelFromExp_CurrentLevel_Zero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetLevelFromExp(1, 0, out _));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    public void GetLevelFromExp_CurrentLevel_Invalid_TooSmall_Throws(int exp, byte currentLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetLevelFromExp(exp, currentLevel, out _));
    }

    [Theory]
    // Characters
    [InlineData(201, 1, false, 1)]
    [InlineData(300, 1, false, 100)]
    [InlineData(int.MaxValue, 1, false, int.MaxValue - maxPlayerExp)]
    [InlineData(201, 2, false, 1)]
    [InlineData(300, 2, false, 100)]
    [InlineData(int.MaxValue, 2, false, int.MaxValue - maxPlayerExp)]
    [InlineData(201, 3, false, 1)]
    [InlineData(300, 3, false, 100)]
    [InlineData(int.MaxValue, 3, false, int.MaxValue - maxPlayerExp)]
    // Mates
    [InlineData(101, 1, true, 1)]
    [InlineData(300, 1, true, 200)]
    [InlineData(int.MaxValue, 1, true, int.MaxValue - maxMateExp)]
    [InlineData(101, 2, true, 1)]
    [InlineData(300, 2, true, 200)]
    [InlineData(int.MaxValue, 2, true, int.MaxValue - maxMateExp)]
    [InlineData(101, 3, true, 1)]
    [InlineData(300, 3, true, 200)]
    [InlineData(int.MaxValue, 3, true, int.MaxValue - maxMateExp)]
    public void GetLevelFromExp_CurrentLevel_Invalid_TooBig_ReturnsMaxLevel(int exp, byte currentLevel, bool mate, int expectedOverflow)
    {
        var level = _cut.GetLevelFromExp(exp, currentLevel, out var overflow, mate);
        var expectedLevel = mate ? _cut.MaxMateLevel : _cut.MaxPlayerLevel;
        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedOverflow, overflow);
    }

    [Theory]
    // Character
    [InlineData(0, false, 1, 1)]
    [InlineData(1, false, 1, 1)]
    [InlineData(99, false, 1, 1)]
    [InlineData(100, false, 1, 2)]
    [InlineData(101, false, 1, 2)]
    [InlineData(199, false, 1, 2)]
    [InlineData(200, false, 1, 3)]

    [InlineData(0, false, 2, 2)] // If you're currently level 2 and have 0 exp, it's expected this will return level 2, because it doesn't check lower than the current level
    [InlineData(1, false, 2, 2)]
    [InlineData(99, false, 2, 2)]
    [InlineData(100, false, 2, 2)]
    [InlineData(101, false, 2, 2)]
    [InlineData(199, false, 2, 2)]
    [InlineData(200, false, 2, 3)]
    // Mate
    [InlineData(0, true, 1, 1)]
    [InlineData(1, true, 1, 1)]
    [InlineData(49, true, 1, 1)]
    [InlineData(50, true, 1, 2)]
    [InlineData(51, true, 1, 2)]
    [InlineData(99, true, 1, 2)]
    [InlineData(100, true, 1, 3)]

    [InlineData(0, true, 2, 2)]
    [InlineData(1, true, 2, 2)]
    [InlineData(49, true, 2, 2)]
    [InlineData(50, true, 2, 2)]
    [InlineData(51, true, 2, 2)]
    [InlineData(99, true, 2, 2)]
    [InlineData(100, true, 2, 3)]
    public void GetLevelFromExp_CurrentLevel_Valid(int exp, bool mate, byte currentLevel, int expectedLevel)
    {
        var level = _cut.GetLevelFromExp(exp, currentLevel, out _, mate);
        Assert.Equal(expectedLevel, level);
    }

    [Theory]
    // Characters
    [InlineData(0, 1, false, 0)]
    [InlineData(5, 1, false, 0)]
    [InlineData(500, 1, false, 0)]
    [InlineData(0, 2, false, 100)]
    [InlineData(0, 3, false, 200)]
    // Mates
    [InlineData(0, 1, true, 0)]
    [InlineData(0, 2, true, 50)]
    [InlineData(0, 3, true, 100)]
    public void GetExpNeededToGivenLevel_Valid(int currentExp, byte targetLevel, bool mate, int expectedExp)
    {
        var exp = _cut.GetExpNeededToGivenLevel(currentExp, targetLevel, mate);
        Assert.Equal(expectedExp, exp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetExpNeededToGivenLevel_Invalid_NegativeExpThrows(bool mate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetExpNeededToGivenLevel(-1, 1, mate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetExpNeededToGivenLevel_Invalid_TargetLevelTooHigh(bool mate)
    {
        var exp = _cut.GetExpNeededToGivenLevel(0, 100, mate);
        Assert.Equal(0, exp);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void GetSkillPointsForLevel_Valid(byte level, int expectedSkillPoints)
    {
        var skillPoints = _cut.GetSkillPointsForLevel(level);
        Assert.Equal(expectedSkillPoints, skillPoints);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(50)]
    [InlineData(byte.MaxValue)]
    public void GetSkillPointsForLevel_Invalid_ReturnsZero(byte level)
    {
        var skillPoints = _cut.GetSkillPointsForLevel(level);
        Assert.Equal(0, skillPoints);
    }

    [Fact]
    public void Load_CallsLoader()
    {
        var mockLoader = new Mock<IExperienceLevelTemplateLoader>(MockBehavior.Strict);
        mockLoader.Setup(l => l.Load())
            .Returns(Enumerable.Empty<ExperienceLevelTemplate>())
            .Verifiable(Times.Once);

        _cut.Load(mockLoader.Object, 1, 1);
        mockLoader.Verify();
    }

    [Fact]
    public void Load_ResetsState()
    {
        // arrange
        var mockLoader1 = new Mock<IExperienceLevelTemplateLoader>(MockBehavior.Strict);
        mockLoader1.Setup(l => l.Load())
            .Returns([
                new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0 },
                new ExperienceLevelTemplate { Level = 2, TotalExp = 100, TotalMateExp = 100 }
            ])
            .Verifiable(Times.Once);

        var mockLoader2 = new Mock<IExperienceLevelTemplateLoader>(MockBehavior.Strict);
        mockLoader2.Setup(l => l.Load())
            .Returns([
                new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0 }
            ])
            .Verifiable(Times.Once);

        _cut.Load(mockLoader1.Object, 2, 2);

        // act
        _cut.Load(mockLoader2.Object, 1, 1);
        var level = _cut.GetLevelFromExp(100, out _, false);

        // assert
        Assert.Equal(1, level); // should be level 1 if the second loader overwrote the first
        Assert.Equal(1, _cut.MaxPlayerLevel);
        Assert.Equal(1, _cut.MaxMateLevel);
        mockLoader1.Verify();
        mockLoader2.Verify();
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(2, 2, 2, 2)]
    [InlineData(3, 3, 2, 2)]
    [InlineData(1, 2, 1, 2)]
    [InlineData(2, 1, 2, 1)]
    [InlineData(3, 1, 2, 1)]
    public void Load_SetsMaxLevel(byte playerLevelCap, byte mateLevelCap, byte expectedMaxPlayerLevel, byte expectedMaxMateLevel)
    {
        var mockLoader = new Mock<IExperienceLevelTemplateLoader>(MockBehavior.Strict);
        mockLoader.Setup(l => l.Load())
            .Returns([
                new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0 },
                new ExperienceLevelTemplate { Level = 2, TotalExp = 100, TotalMateExp = 100 }
            ])
            .Verifiable(Times.Once);

        _cut.Load(mockLoader.Object, playerLevelCap, mateLevelCap);

        mockLoader.Verify();
        Assert.Equal(expectedMaxPlayerLevel, _cut.MaxPlayerLevel);
        Assert.Equal(expectedMaxMateLevel, _cut.MaxMateLevel);
    }

    private void SetupExperienceManager(ExperienceLevelTemplate[] levelTemplates)
    {
        var mockLoader = new Mock<IExperienceLevelTemplateLoader>(MockBehavior.Strict);
        mockLoader.Setup(x => x.Load()).Returns(levelTemplates);
        _cut.Load(mockLoader.Object, (byte)levelTemplates.Length, (byte)levelTemplates.Length);
    }
}
