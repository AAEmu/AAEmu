using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemChangeMappingRulesTests
{
    [Test]
    public async Task MatchesSourceGrade_Negative_AcceptsAnyGrade()
    {
        await Assert.That(ItemChangeMappingRules.MatchesSourceGrade(-1, 0)).IsTrue();
        await Assert.That(ItemChangeMappingRules.MatchesSourceGrade(-1, 12)).IsTrue();
    }

    [Test]
    public async Task MatchesSourceGrade_ExactMinimum_Accepts()
    {
        // Rank-3 leap recipes ship source_grade_id 6 (Unique).
        await Assert.That(ItemChangeMappingRules.MatchesSourceGrade(6, 6)).IsTrue();
    }

    [Test]
    public async Task MatchesSourceGrade_AboveMinimum_Accepts()
    {
        // Live: Brilliant Explorer dagger grade 8 still offered Rank-3 awaken in the client UI.
        await Assert.That(ItemChangeMappingRules.MatchesSourceGrade(6, 8)).IsTrue();
    }

    [Test]
    public async Task MatchesSourceGrade_BelowMinimum_Rejects()
    {
        await Assert.That(ItemChangeMappingRules.MatchesSourceGrade(6, 5)).IsFalse();
        await Assert.That(ItemChangeMappingRules.MatchesSourceGrade(6, 0)).IsFalse();
    }
}
