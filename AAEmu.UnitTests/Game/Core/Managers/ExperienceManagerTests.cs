using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ExperienceManagerTests
{
    private ExperienceManager _cut = null!;
    private const int maxPlayerExp = 200;
    private const int maxMateExp = 100;

    [Before(Test)]
    public void Setup()
    {
        _cut = new ExperienceManager();
        SetupExperienceManager([
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0, SkillPoints = 1 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100, TotalMateExp = 50, SkillPoints = 2 },
            new ExperienceLevelTemplate { Level = 3, TotalExp = 200, TotalMateExp = 100, SkillPoints = 3 }
        ]);
    }

    [Test]
    public async Task MaxPlayerLevelGreaterThanZero()
    {
        await Assert.That(_cut.MaxPlayerLevel > 0).IsTrue();
    }

    [Test]
    public async Task MaxMateLevelGreaterThanZero()
    {
        await Assert.That(_cut.MaxMateLevel > 0).IsTrue();
    }

    [Test]
    // Character
    [Arguments(0, false, 0)] // Level 0 is invalid for character, so should return 0 exp
    [Arguments(1, false, 0)]
    [Arguments(2, false, 100)]
    [Arguments(3, false, 200)]
    [Arguments(4, false, 0)] // Level 3 is invalid for character, so should return 0 exp
    // Mate
    [Arguments(0, true, 0)] // Level 0 is invalid for mate, so should return 0 exp
    [Arguments(1, true, 0)]
    [Arguments(2, true, 50)]
    [Arguments(3, true, 100)]
    [Arguments(4, true, 0)] // Level 3 is invalid for mate, so should return 0 exp
    public async Task GetExpForLevel(byte level, bool mate, int expectedExp)
    {
        var exp = _cut.GetExpForLevel(level, mate);
        await Assert.That(exp).IsEqualTo(expectedExp);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public void GetLevelFromExp_Invalid_TooSmall_Throws(int exp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetLevelFromExp(exp, out _));
    }

    [Test]
    // Characters
    [Arguments(201, false)]
    [Arguments(300, false)]
    [Arguments(int.MaxValue, false)]
    // Mates
    [Arguments(101, true)]
    [Arguments(300, true)]
    [Arguments(int.MaxValue, true)]
    public async Task GetLevelFromExp_Invalid_TooBig_ReturnsMaxLevel(int exp, bool mate)
    {
        var level = _cut.GetLevelFromExp(exp, out var overflow, mate);

        var expectedLevel = mate ? _cut.MaxMateLevel : _cut.MaxPlayerLevel;
        var expectedOverflow = exp - (mate ? maxMateExp : maxPlayerExp);

        await Assert.That(level).IsEqualTo(expectedLevel);
        await Assert.That(overflow).IsEqualTo(expectedOverflow);
    }

    [Test]
    // Character
    [Arguments(0, false, 1, 0)]
    [Arguments(1, false, 1, 1)]
    [Arguments(99, false, 1, 99)]
    [Arguments(100, false, 2, 0)]
    [Arguments(101, false, 2, 1)]
    [Arguments(199, false, 2, 99)]
    [Arguments(200, false, 3, 0)]
    // Mate
    [Arguments(0, true, 1, 0)]
    [Arguments(1, true, 1, 1)]
    [Arguments(49, true, 1, 49)]
    [Arguments(50, true, 2, 0)]
    [Arguments(51, true, 2, 1)]
    [Arguments(99, true, 2, 49)]
    [Arguments(100, true, 3, 0)]
    public async Task GetLevelFromExp_Valid(int exp, bool mate, byte expectedLevel, int expectedOverflow)
    {
        var level = _cut.GetLevelFromExp(exp, out var overflow, mate);
        await Assert.That(level).IsEqualTo(expectedLevel);
        await Assert.That(overflow).IsEqualTo(expectedOverflow);
    }

    [Test]
    public void GetLevelFromExp_CurrentLevel_Zero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetLevelFromExp(1, 0, out _));
    }

    [Test]
    [Arguments(-1, 1)]
    [Arguments(int.MinValue, 1)]
    public void GetLevelFromExp_CurrentLevel_Invalid_TooSmall_Throws(int exp, byte currentLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetLevelFromExp(exp, currentLevel, out _));
    }

    [Test]
    // Characters
    [Arguments(201, 1, false, 1)]
    [Arguments(300, 1, false, 100)]
    [Arguments(int.MaxValue, 1, false, int.MaxValue - maxPlayerExp)]
    [Arguments(201, 2, false, 1)]
    [Arguments(300, 2, false, 100)]
    [Arguments(int.MaxValue, 2, false, int.MaxValue - maxPlayerExp)]
    [Arguments(201, 3, false, 1)]
    [Arguments(300, 3, false, 100)]
    [Arguments(int.MaxValue, 3, false, int.MaxValue - maxPlayerExp)]
    // Mates
    [Arguments(101, 1, true, 1)]
    [Arguments(300, 1, true, 200)]
    [Arguments(int.MaxValue, 1, true, int.MaxValue - maxMateExp)]
    [Arguments(101, 2, true, 1)]
    [Arguments(300, 2, true, 200)]
    [Arguments(int.MaxValue, 2, true, int.MaxValue - maxMateExp)]
    [Arguments(101, 3, true, 1)]
    [Arguments(300, 3, true, 200)]
    [Arguments(int.MaxValue, 3, true, int.MaxValue - maxMateExp)]
    public async Task GetLevelFromExp_CurrentLevel_Invalid_TooBig_ReturnsMaxLevel(int exp, byte currentLevel, bool mate, int expectedOverflow)
    {
        var level = _cut.GetLevelFromExp(exp, currentLevel, out var overflow, mate);
        var expectedLevel = mate ? _cut.MaxMateLevel : _cut.MaxPlayerLevel;
        await Assert.That(level).IsEqualTo(expectedLevel);
        await Assert.That(overflow).IsEqualTo(expectedOverflow);
    }

    [Test]
    // Character
    [Arguments(0, false, 1, 1)]
    [Arguments(1, false, 1, 1)]
    [Arguments(99, false, 1, 1)]
    [Arguments(100, false, 1, 2)]
    [Arguments(101, false, 1, 2)]
    [Arguments(199, false, 1, 2)]
    [Arguments(200, false, 1, 3)]

    [Arguments(0, false, 2, 2)] // If you're currently level 2 and have 0 exp, it's expected this will return level 2, because it doesn't check lower than the current level
    [Arguments(1, false, 2, 2)]
    [Arguments(99, false, 2, 2)]
    [Arguments(100, false, 2, 2)]
    [Arguments(101, false, 2, 2)]
    [Arguments(199, false, 2, 2)]
    [Arguments(200, false, 2, 3)]
    // Mate
    [Arguments(0, true, 1, 1)]
    [Arguments(1, true, 1, 1)]
    [Arguments(49, true, 1, 1)]
    [Arguments(50, true, 1, 2)]
    [Arguments(51, true, 1, 2)]
    [Arguments(99, true, 1, 2)]
    [Arguments(100, true, 1, 3)]

    [Arguments(0, true, 2, 2)]
    [Arguments(1, true, 2, 2)]
    [Arguments(49, true, 2, 2)]
    [Arguments(50, true, 2, 2)]
    [Arguments(51, true, 2, 2)]
    [Arguments(99, true, 2, 2)]
    [Arguments(100, true, 2, 3)]
    public async Task GetLevelFromExp_CurrentLevel_Valid(int exp, bool mate, byte currentLevel, byte expectedLevel)
    {
        var level = _cut.GetLevelFromExp(exp, currentLevel, out _, mate);
        await Assert.That(level).IsEqualTo(expectedLevel);
    }

    [Test]
    // Characters
    [Arguments(0, 1, false, 0)]
    [Arguments(5, 1, false, 0)]
    [Arguments(500, 1, false, 0)]
    [Arguments(0, 2, false, 100)]
    [Arguments(0, 3, false, 200)]
    // Mates
    [Arguments(0, 1, true, 0)]
    [Arguments(0, 2, true, 50)]
    [Arguments(0, 3, true, 100)]
    public async Task GetExpNeededToGivenLevel_Valid(int currentExp, byte targetLevel, bool mate, int expectedExp)
    {
        var exp = _cut.GetExpNeededToGivenLevel(currentExp, targetLevel, mate);
        await Assert.That(exp).IsEqualTo(expectedExp);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void GetExpNeededToGivenLevel_Invalid_NegativeExpThrows(bool mate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cut.GetExpNeededToGivenLevel(-1, 1, mate));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetExpNeededToGivenLevel_Invalid_TargetLevelTooHigh(bool mate)
    {
        var exp = _cut.GetExpNeededToGivenLevel(0, 100, mate);
        await Assert.That(exp).IsEqualTo(0);
    }

    [Test]
    [Arguments(1, 1)]
    [Arguments(2, 2)]
    [Arguments(3, 3)]
    public async Task GetSkillPointsForLevel_Valid(byte level, int expectedSkillPoints)
    {
        var skillPoints = _cut.GetSkillPointsForLevel(level);
        await Assert.That(skillPoints).IsEqualTo(expectedSkillPoints);
    }

    [Test]
    [Arguments(4)]
    [Arguments(50)]
    [Arguments(byte.MaxValue)]
    public async Task GetSkillPointsForLevel_Invalid_ReturnsZero(byte level)
    {
        var skillPoints = _cut.GetSkillPointsForLevel(level);
        await Assert.That(skillPoints).IsEqualTo(0);
    }

    [Test]
    public void Load_CallsLoader()
    {
        var mockLoader = Mock.Of<IExperienceLevelTemplateLoader>();
        mockLoader.Load().Returns(Enumerable.Empty<ExperienceLevelTemplate>());

        _cut.Load(mockLoader.Object, 1, 1);

        mockLoader.Load().WasCalled(Times.Once);
    }

    [Test]
    public async Task Load_ResetsState()
    {
        // arrange
        var mockLoader1 = Mock.Of<IExperienceLevelTemplateLoader>();
        mockLoader1.Load().Returns([
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100, TotalMateExp = 100 }
        ]);

        var mockLoader2 = Mock.Of<IExperienceLevelTemplateLoader>();
        mockLoader2.Load().Returns([
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0 }
        ]);

        _cut.Load(mockLoader1.Object, 2, 2);

        // act
        _cut.Load(mockLoader2.Object, 1, 1);
        var level = _cut.GetLevelFromExp(100, out _, false);

        // assert
        await Assert.That(level).IsEqualTo((byte)1); // should be level 1 if the second loader overwrote the first
        await Assert.That(_cut.MaxPlayerLevel).IsEqualTo((byte)1);
        await Assert.That(_cut.MaxMateLevel).IsEqualTo((byte)1);
        mockLoader1.Load().WasCalled(Times.Once);
        mockLoader2.Load().WasCalled(Times.Once);
    }

    [Test]
    [Arguments(1, 1, 1, 1)]
    [Arguments(2, 2, 2, 2)]
    [Arguments(3, 3, 2, 2)]
    [Arguments(1, 2, 1, 2)]
    [Arguments(2, 1, 2, 1)]
    [Arguments(3, 1, 2, 1)]
    public async Task Load_SetsMaxLevel(byte playerLevelCap, byte mateLevelCap, byte expectedMaxPlayerLevel, byte expectedMaxMateLevel)
    {
        var mockLoader = Mock.Of<IExperienceLevelTemplateLoader>();
        mockLoader.Load().Returns([
            new ExperienceLevelTemplate { Level = 1, TotalExp = 0, TotalMateExp = 0 },
            new ExperienceLevelTemplate { Level = 2, TotalExp = 100, TotalMateExp = 100 }
        ]);

        _cut.Load(mockLoader.Object, playerLevelCap, mateLevelCap);

        mockLoader.Load().WasCalled(Times.Once);
        await Assert.That(_cut.MaxPlayerLevel).IsEqualTo(expectedMaxPlayerLevel);
        await Assert.That(_cut.MaxMateLevel).IsEqualTo(expectedMaxMateLevel);
    }

    private void SetupExperienceManager(ExperienceLevelTemplate[] levelTemplates)
    {
        var mockLoader = Mock.Of<IExperienceLevelTemplateLoader>();
        mockLoader.Load().Returns(levelTemplates);
        _cut.Load(mockLoader.Object, (byte)levelTemplates.Length, (byte)levelTemplates.Length);
    }
}
