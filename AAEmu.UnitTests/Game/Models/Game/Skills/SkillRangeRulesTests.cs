using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The range band as the client judges it: too close at or inside min_range, too far beyond
/// max_range, and no measurement when the target is the caster. The bands are real rows:
/// 12161 돌진 (hostile, 5..15), 13281 다발 사격 (the player's ground skill, 10..50), 46100 술통 기뢰 (self,
/// 3..0), holdables 0 fist (0..3) and 19 bow (0..20), and skill_modifiers 2144 (+4 min_range on tag 3849).
/// </summary>
public class SkillRangeRulesTests
{
    private static readonly SkillRangeRules.Band Charge = SkillRangeRules.Band.Of(5, 15);
    private static readonly SkillRangeRules.Band Volley = SkillRangeRules.Band.Of(10, 50);
    private static readonly SkillRangeRules.Band Fist = SkillRangeRules.Band.Of(0, 3);
    private static readonly SkillRangeRules.Band Bow = SkillRangeRules.Band.Of(0, 20);

    [Test]
    public async Task AtExactlyMinRange_IsTooClose()
    {
        // Passes only when min < distance, so the boundary itself is refused.
        await Assert.That(SkillRangeRules.Check(5.0, Charge)).IsEqualTo(SkillResult.TooCloseRange);
        await Assert.That(SkillRangeRules.Check(10.0, Volley)).IsEqualTo(SkillResult.TooCloseRange);
    }

    [Test]
    public async Task JustBeyondMinRange_IsInRange()
    {
        await Assert.That(SkillRangeRules.Check(5.01, Charge)).IsNull();
        await Assert.That(SkillRangeRules.Check(10.01, Volley)).IsNull();
    }

    [Test]
    public async Task InsideMinRange_IsTooClose()
    {
        await Assert.That(SkillRangeRules.Check(4.0, Charge)).IsEqualTo(SkillResult.TooCloseRange);
        await Assert.That(SkillRangeRules.Check(0.0, Charge)).IsEqualTo(SkillResult.TooCloseRange);
    }

    [Test]
    public async Task AtExactlyMaxRange_IsInRange()
    {
        // The far side is inclusive: refuses only when max < distance.
        await Assert.That(SkillRangeRules.Check(15.0, Charge)).IsNull();
        await Assert.That(SkillRangeRules.Check(50.0, Volley)).IsNull();
    }

    [Test]
    public async Task BeyondMaxRange_IsTooFar()
    {
        await Assert.That(SkillRangeRules.Check(15.01, Charge)).IsEqualTo(SkillResult.TooFarRange);
        await Assert.That(SkillRangeRules.Check(50.5, Volley)).IsEqualTo(SkillResult.TooFarRange);
    }

    [Test]
    public async Task NoMinimum_IsNeverTooClose()
    {
        // The weapon bands start at 0, and only runs the near check when min > 0.
        await Assert.That(SkillRangeRules.Check(0.0, Fist)).IsNull();
        await Assert.That(SkillRangeRules.Check(3.0, Fist)).IsNull();
        await Assert.That(SkillRangeRules.Check(3.01, Fist)).IsEqualTo(SkillResult.TooFarRange);
        await Assert.That(SkillRangeRules.Check(20.0, Bow)).IsNull();
        await Assert.That(SkillRangeRules.Check(20.01, Bow)).IsEqualTo(SkillResult.TooFarRange);
    }

    [Test]
    public async Task UnboundedMax_LeavesOnlyTheMinimum()
    {
        // A placement or plot_only cast with max_range 0 keeps its old permissive far side.
        var band = SkillRangeRules.Band.Of(5, 0, maxUnbounded: true);

        await Assert.That(SkillRangeRules.Check(999.0, band)).IsNull();
        await Assert.That(SkillRangeRules.Check(5.0, band)).IsEqualTo(SkillResult.TooCloseRange);
    }

    [Test]
    public async Task MaxBelowMin_IsLiftedHalfAMetreAboveMin()
    {
        // 46100 술통 기뢰 is the one row shipped this way (3..0); lifts it to min + 0.5.
        var band = SkillRangeRules.Band.Of(3, 0);

        await Assert.That(band.Max).IsEqualTo(3.5);
        await Assert.That(SkillRangeRules.Check(3.5, band)).IsNull();
        await Assert.That(SkillRangeRules.Check(3.6, band)).IsEqualTo(SkillResult.TooFarRange);
    }

    [Test]
    public async Task NegativeMin_IsNoMin()
    {
        await Assert.That(SkillRangeRules.Band.Of(-1, 4).Min).IsEqualTo(0.0);
        await Assert.That(SkillRangeRules.Check(0.0, SkillRangeRules.Band.Of(-1, 4))).IsNull();
    }

    [Test]
    public async Task TooCloseIsJudgedBeforeTooFar()
    {
        var band = SkillRangeRules.Band.Of(20, 10);

        await Assert.That(SkillRangeRules.Check(5.0, band)).IsEqualTo(SkillResult.TooCloseRange);
    }

    [Test]
    public async Task ASelfCast_IsNeverMeasured()
    {
        // Calls ValidateLocation only for a target that is not the caster, which is what lets
        // the 123 self-target skills with a minimum (51 of them NPC kit rows) fire at all.
        await Assert.That(SkillRangeRules.Measures(casterObjId: 7, targetObjId: 7)).IsFalse();
        await Assert.That(SkillRangeRules.Measures(casterObjId: 7, targetObjId: 8)).IsTrue();
    }

    [Test]
    public async Task AMinRangeModifier_MovesTheNearBoundary()
    {
        // skill_modifiers 2144: buff 27701 adds 4 to min_range on tag 3849; attribute 17 is applied to the
        // minimum before the band is judged.
        var band = SkillRangeRules.Band.Of(5 + 4, 15);

        await Assert.That(SkillRangeRules.Check(9.0, band)).IsEqualTo(SkillResult.TooCloseRange);
        await Assert.That(SkillRangeRules.Check(9.01, band)).IsNull();
    }
}
