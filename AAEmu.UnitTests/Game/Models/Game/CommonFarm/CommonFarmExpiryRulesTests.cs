using AAEmu.Game.Models.Game.CommonFarm;

namespace AAEmu.UnitTests.Game.Models.Game.CommonFarm;

/// <summary>
/// The per-crop decision the farm expiry pass makes every minute.
/// </summary>
/// <remarks>
/// The trap this pins is a missing protection window collapsing into a zero-length one. A zero-length
/// window is not an empty window: it retires the crop on the first pass after it is planted, so a
/// content gap destroys what it was meant to protect.
/// </remarks>
public class CommonFarmExpiryRulesTests
{
    private static readonly DateTime Planted = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task AMissingProtectionWindowKeepsTheCropAtEveryAge()
    {
        // The regression, as a table. Content naming no doodad_groups row for the crop's group leaves
        // the window's length unknown, and an unknown age is not an old age.
        foreach (var seconds in new uint[] { 0, 1, 3600, 86400, 1_000_000 })
        {
            var decision = CommonFarmExpiryRules.Evaluate(
                guardTimeConfigured: false, guardSeconds: seconds,
                plantTimeUtc: Planted, nowUtc: Planted.AddYears(10));

            await Assert.That(decision).IsEqualTo(CommonFarmExpiryDecision.GuardTimeNotConfigured);
        }
    }

    [Test]
    public async Task AMissingProtectionWindowIsNotAnExpiredCrop()
    {
        // The two answers must not be the same value, or a caller that only tests for "expired" and
        // a caller that only tests for "kept" would both be right about the same crop.
        var unknown = CommonFarmExpiryRules.Evaluate(false, 0, Planted, Planted.AddYears(10));
        var expired = CommonFarmExpiryRules.Evaluate(true, 1, Planted, Planted.AddYears(10));

        await Assert.That(unknown).IsNotEqualTo(CommonFarmExpiryDecision.Expire);
        await Assert.That(expired).IsEqualTo(CommonFarmExpiryDecision.Expire);
    }

    [Test]
    public async Task ACropInsideItsWindowIsKeptAndOnePastItIsExpired()
    {
        // A 60-second window: the boundary is the whole rule, so both sides of it are asserted.
        var planted = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var justInside = CommonFarmExpiryRules.Evaluate(true, 60, planted, planted.AddSeconds(59));
        var exactlyAt = CommonFarmExpiryRules.Evaluate(true, 60, planted, planted.AddSeconds(60));
        var past = CommonFarmExpiryRules.Evaluate(true, 60, planted, planted.AddSeconds(61));

        await Assert.That(justInside).IsEqualTo(CommonFarmExpiryDecision.Keep);
        await Assert.That(exactlyAt).IsEqualTo(CommonFarmExpiryDecision.Keep);
        await Assert.That(past).IsEqualTo(CommonFarmExpiryDecision.Expire);
    }

    [Test]
    public async Task AZeroLengthWindowStillProtectsTheInstantItWasPlanted()
    {
        // A configured zero is a real content value and means "no protection at all" — but it is
        // still a window, and the crop is protected at the instant it was planted. Answering
        // "expired" here would retire a crop the same millisecond it arrived.
        var planted = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var atPlanting = CommonFarmExpiryRules.Evaluate(true, 0, planted, planted);
        var later = CommonFarmExpiryRules.Evaluate(true, 0, planted, planted.AddSeconds(1));

        await Assert.That(atPlanting).IsEqualTo(CommonFarmExpiryDecision.Keep);
        await Assert.That(later).IsEqualTo(CommonFarmExpiryDecision.Expire);
    }

    [Test]
    public async Task ACropWithNoPlantingTimeIsKeptRatherThanTreatedAsAncient()
    {
        // A load that left the column empty must not expire the crop on the first pass: "never
        // planted" would otherwise compare as "planted at the start of time".
        var decision = CommonFarmExpiryRules.Evaluate(true, 1, default, DateTime.UtcNow);

        await Assert.That(decision).IsEqualTo(CommonFarmExpiryDecision.Keep);
    }

    [Test]
    public async Task ACropWithNoPlantingTimeIsStillRefusedWhenTheWindowIsUnknown()
    {
        // The unknown-window answer comes first and is about content, not about the crop, so it is
        // the same answer whichever way the age question would have gone.
        var decision = CommonFarmExpiryRules.Evaluate(false, 0, default, DateTime.UtcNow);

        await Assert.That(decision).IsEqualTo(CommonFarmExpiryDecision.GuardTimeNotConfigured);
    }
}
