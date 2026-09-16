using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="AoeDiminishingRules"/> and <see cref="AoeDiminishingTracker"/>: the ten
/// <c>aoe_diminishings</c> rates a unit's successive area hits walk down.
/// </summary>
[NotInParallel]
public class AoeDiminishingRulesTests
{
    /// <summary>The shipped table, ids 1..10.</summary>
    private static readonly int[] Shipped = [100, 95, 90, 85, 80, 75, 70, 65, 60, 50];

    private const uint UnitObjId = 501;
    private const uint SkillId = 11939; // 불의 비, target_area_count 3 / target_area_radius 10, plot 1

    [Test]
    public async Task NextRate_WithoutATable_IsTheNeutralRate()
    {
        // No table at all: the first rate has to be 100 so the multiplier is exactly 1.0f.
        await Assert.That(AoeDiminishingRules.NextRate(0, DateTime.MinValue, DateTime.UtcNow, []))
            .IsEqualTo(100);
        await Assert.That(AoeDiminishingRules.NextRate(0, DateTime.MinValue, DateTime.UtcNow, null))
            .IsEqualTo(100);
    }

    [Test]
    public async Task NextRate_ThreeHitsInsideTheWindow_Are100Then95Then90()
    {
        // The acceptance curve.
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var first = AoeDiminishingRules.NextRate(0, DateTime.MinValue, start, Shipped);
        var second = AoeDiminishingRules.NextRate(first, start, start.AddSeconds(1), Shipped);
        var third = AoeDiminishingRules.NextRate(second, start.AddSeconds(1), start.AddSeconds(2), Shipped);

        await Assert.That(first).IsEqualTo(100);
        await Assert.That(second).IsEqualTo(95);
        await Assert.That(third).IsEqualTo(90);
    }

    [Test]
    public async Task RateMultiplier_ConvertsTheRateToAFactor()
    {
        await Assert.That(AoeDiminishingRules.RateMultiplier(100)).IsEqualTo(1.0f);
        await Assert.That(AoeDiminishingRules.RateMultiplier(95)).IsEqualTo(0.95f);
        await Assert.That(AoeDiminishingRules.RateMultiplier(50)).IsEqualTo(0.5f);
    }

    [Test]
    public async Task NextRate_AfterTheWindow_Restarts()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var restarted = AoeDiminishingRules.NextRate(
            95, start, start.AddSeconds(AoeDiminishingRules.WindowSeconds + 0.5), Shipped);

        await Assert.That(restarted).IsEqualTo(100);
    }

    [Test]
    public async Task NextRate_AtTheEndOfTheWindow_DoesNotRestart()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var next = AoeDiminishingRules.NextRate(
            95, start, start.AddSeconds(AoeDiminishingRules.WindowSeconds), Shipped);

        await Assert.That(next).IsEqualTo(90);
    }

    [Test]
    public async Task NextRate_PastTheLastRow_RepeatsIt()
    {
        // The eleventh hit is 50, not 100: the table is a decay, and wrapping would make it cheaper.
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.That(AoeDiminishingRules.NextRate(50, start, start.AddSeconds(1), Shipped)).IsEqualTo(50);
    }

    [Test]
    public async Task NextRate_WalksEveryShippedRow()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rate = 0;

        var walked = new List<int>();
        for (var i = 0; i < Shipped.Length; i++)
        {
            rate = AoeDiminishingRules.NextRate(rate, now.AddSeconds(i), now.AddSeconds(i + 1), Shipped);
            walked.Add(rate);
        }

        await Assert.That(walked).IsEquivalentTo(Shipped);
    }

    [Test]
    public async Task NextRate_WithARateTheTableDoesNotHave_Restarts()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.That(AoeDiminishingRules.NextRate(77, start, start.AddSeconds(1), Shipped)).IsEqualTo(100);
    }

    [Test]
    public async Task IsAreaSkill_ReadsTheTwoGeometryColumns()
    {
        // The columns Skill gathers its area targets with. A single-target skill is not an area skill.
        await Assert.That(AoeDiminishingRules.IsAreaSkill(targetAreaCount: 1, targetAreaRadius: 0)).IsFalse();
        await Assert.That(AoeDiminishingRules.IsAreaSkill(targetAreaCount: 3, targetAreaRadius: 10)).IsTrue();
        await Assert.That(AoeDiminishingRules.IsAreaSkill(targetAreaCount: 1, targetAreaRadius: 31)).IsTrue();
        await Assert.That(AoeDiminishingRules.IsAreaSkill(targetAreaCount: 20, targetAreaRadius: 0)).IsTrue();
    }

    [Test]
    public async Task Diminishes_NeedsTheFlagOnTheSkillOrOnThePlotCast()
    {
        // plot_events.aoe_diminishing is on 733 of 51,178 rows and is the content's own marker.
        await Assert.That(AoeDiminishingRules.Diminishes(isAreaSkill: true, plotFlagged: true, plotCastFlagged: false))
            .IsTrue();
        await Assert.That(AoeDiminishingRules.Diminishes(isAreaSkill: false, plotFlagged: false, plotCastFlagged: true))
            .IsTrue();
        await Assert.That(AoeDiminishingRules.Diminishes(isAreaSkill: true, plotFlagged: false, plotCastFlagged: false))
            .IsFalse();
        await Assert.That(AoeDiminishingRules.Diminishes(isAreaSkill: false, plotFlagged: true, plotCastFlagged: false))
            .IsFalse();
    }

    [Test]
    public async Task Tracker_ThreeHitsOnOneSkill_Are100Then95Then90()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AoeDiminishingTracker.Reset(UnitObjId);

        var rates = new List<int>
        {
            AoeDiminishingTracker.Advance(UnitObjId, SkillId, now, Shipped),
            AoeDiminishingTracker.Advance(UnitObjId, SkillId, now.AddSeconds(1), Shipped),
            AoeDiminishingTracker.Advance(UnitObjId, SkillId, now.AddSeconds(2), Shipped)
        };

        await Assert.That(rates).IsEquivalentTo([100, 95, 90]);
    }

    [Test]
    public async Task Tracker_KeepsOneCounterPerSkill()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AoeDiminishingTracker.Reset(UnitObjId);

        AoeDiminishingTracker.Advance(UnitObjId, SkillId, now, Shipped);
        AoeDiminishingTracker.Advance(UnitObjId, SkillId, now.AddSeconds(1), Shipped);

        // A different area skill on the same unit starts at the first rate.
        var other = AoeDiminishingTracker.Advance(UnitObjId, SkillId + 1, now.AddSeconds(1), Shipped);

        await Assert.That(other).IsEqualTo(100);
        await Assert.That(AoeDiminishingTracker.Peek(UnitObjId, SkillId)).IsEqualTo(95);
    }

    [Test]
    public async Task Tracker_KeepsOneCounterPerUnit()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AoeDiminishingTracker.Reset(UnitObjId);
        AoeDiminishingTracker.Reset(UnitObjId + 1);

        AoeDiminishingTracker.Advance(UnitObjId, SkillId, now, Shipped);
        AoeDiminishingTracker.Advance(UnitObjId, SkillId, now.AddSeconds(1), Shipped);

        var other = AoeDiminishingTracker.Advance(UnitObjId + 1, SkillId, now.AddSeconds(1), Shipped);

        await Assert.That(other).IsEqualTo(100);
    }

    [Test]
    public async Task Tracker_ResetSkipsBackToTheFirstRate()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AoeDiminishingTracker.Reset(UnitObjId);

        AoeDiminishingTracker.Advance(UnitObjId, SkillId, now, Shipped);
        AoeDiminishingTracker.Advance(UnitObjId, SkillId, now.AddSeconds(1), Shipped);
        AoeDiminishingTracker.Reset(UnitObjId);

        var afterReset = AoeDiminishingTracker.Advance(UnitObjId, SkillId, now.AddSeconds(2), Shipped);

        await Assert.That(afterReset).IsEqualTo(100);
    }

    [Test]
    public async Task Tracker_ResetWithoutASkill_ClearsEveryCounterOfTheUnit()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AoeDiminishingTracker.Reset(UnitObjId);

        AoeDiminishingTracker.Advance(UnitObjId, SkillId, now, Shipped);
        AoeDiminishingTracker.Advance(UnitObjId, SkillId + 1, now, Shipped);
        AoeDiminishingTracker.Reset(UnitObjId);

        await Assert.That(AoeDiminishingTracker.Peek(UnitObjId, SkillId)).IsEqualTo(0);
        await Assert.That(AoeDiminishingTracker.Peek(UnitObjId, SkillId + 1)).IsEqualTo(0);
    }

    [Test]
    public async Task Table_IsEmptyUntilContentIsLoaded()
    {
        // A run with no table (a test host, a load that failed) must not diminish anything: every caller
        // checks IsLoaded first and an empty table means a factor of exactly 1.0f.
        await Assert.That(AoeDiminishingTable.IsLoaded).IsFalse();
        await Assert.That(AoeDiminishingTable.Rates).IsEmpty();
    }

    [Test]
    public async Task Table_SealOrdersTheShippedRowsById()
    {
        AoeDiminishingTable.Clear();
        try
        {
            // Loaded out of order on purpose, the way a SQLite SELECT without ORDER BY may answer.
            AoeDiminishingTable.Add(3, 90);
            AoeDiminishingTable.Add(1, 100);
            AoeDiminishingTable.Add(2, 95);
            AoeDiminishingTable.Seal();

            await Assert.That(AoeDiminishingTable.Rates).IsEquivalentTo([100, 95, 90]);
        }
        finally
        {
            AoeDiminishingTable.Clear();
            AoeDiminishingTable.Seal();
        }
    }
}
