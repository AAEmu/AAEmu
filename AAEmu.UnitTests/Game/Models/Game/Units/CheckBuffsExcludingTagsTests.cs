using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// Edge-case tests for <see cref="Buffs.CheckBuffsExcludingTags"/>. The matching path
/// hits <c>SkillManager.Instance.GetBuffTags</c>, which requires DB-backed singleton
/// state — those scenarios are covered in integration tests. These tests cover the
/// pure return-false paths the singleton is never reached on.
/// </summary>
public class CheckBuffsExcludingTagsTests
{
    private Buffs _buffs;

    [Before(Test)]
    public void Setup()
    {
        _buffs = new Buffs();
    }

    [Test]
    public async Task ReturnsFalse_WhenIdListNull()
    {
        var result = _buffs.CheckBuffsExcludingTags(null, [42u]);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ReturnsFalse_WhenIdListEmpty()
    {
        var result = _buffs.CheckBuffsExcludingTags([], [42u]);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ReturnsFalse_WhenNoActiveEffects()
    {
        // Owner has no buffs at all → no effect can match the id list
        var result = _buffs.CheckBuffsExcludingTags([1u, 2u, 3u], [42u]);
        await Assert.That(result).IsFalse();
    }
}
