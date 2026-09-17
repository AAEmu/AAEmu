using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Models.Game.Rankings;

public class RankPayoutsTests
{
    private static RankTier Tier(uint id, int from, int to, uint item = 0, int grade = 0, bool isLocal = false)
    {
        return new RankTier
        {
            Id = id,
            RankId = 42,
            IsLocal = isLocal,
            ScopeFrom = from,
            ScopeTo = to,
            RewardItemId = item,
            RewardItemCount = item == 0 ? 0 : 1,
            RewardItemGradeId = grade
        };
    }

    private static RankPlace Place(uint position, ulong holder)
    {
        return new RankPlace(new RankScore
        {
            RankId = 42,
            HolderKind = RankHolderKind.Character,
            HolderId = holder,
            AccountId = 3,
            WorldId = 1,
            Value = 100,
            PeriodStartUtc = DateTime.UnixEpoch
        }, position);
    }

    [Test]
    public async Task TierFor_FindsTheBandAPlaceFallsIn()
    {
        var tiers = new[] { Tier(1, 1, 1), Tier(2, 2, 5), Tier(3, 6, 20) };

        await Assert.That(RankPayouts.TierFor(tiers, 1, isLocal: false).Id).IsEqualTo(1u);
        await Assert.That(RankPayouts.TierFor(tiers, 5, isLocal: false).Id).IsEqualTo(2u);
        await Assert.That(RankPayouts.TierFor(tiers, 6, isLocal: false).Id).IsEqualTo(3u);
        await Assert.That(RankPayouts.TierFor(tiers, 21, isLocal: false)).IsNull();
    }

    [Test]
    public async Task TierFor_ReadsTheDivisionItWasAskedFor()
    {
        // The shipped tiers come in pairs and only one of the pair carries the reward; the window's
        // default division is the whole server.
        var tiers = new[] { Tier(1, 1, 1, item: 54160, grade: 12), Tier(2, 1, 1, isLocal: true) };

        await Assert.That(RankPayouts.TierFor(tiers, 1, isLocal: false).RewardItemId).IsEqualTo(54160u);
        await Assert.That(RankPayouts.TierFor(tiers, 1, isLocal: true).RewardItemId).IsEqualTo(0u);
    }

    [Test]
    public async Task Previous_IsTheWindowThatEndedWhenThisOneOpened()
    {
        var september = new RankPeriod(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var august = RankPayouts.Previous(RankPeriods.Monthly, RankPeriods.NoDay, september);

        // August is 31 days and September is 30, so the previous window cannot come from a subtraction.
        await Assert.That(august.StartUtc).IsEqualTo(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(august.EndUtc).IsEqualTo(september.StartUtc);
    }

    [Test]
    public async Task Previous_AcrossTheYearBoundary()
    {
        var january = new RankPeriod(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        var december = RankPayouts.Previous(RankPeriods.Monthly, RankPeriods.NoDay, january);

        await Assert.That(december.StartUtc).IsEqualTo(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(december.EndUtc).IsEqualTo(january.StartUtc);
    }

    [Test]
    public async Task Previous_OfAWeek_IsTheWeekBefore()
    {
        var week = new RankPeriod(new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));

        var before = RankPayouts.Previous(RankPeriods.Weekly, 7, week);

        await Assert.That(before.StartUtc).IsEqualTo(new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(before.EndUtc).IsEqualTo(week.StartUtc);
    }

    [Test]
    public async Task Plan_PaysEveryPlaceItsTierNames()
    {
        var board = new RankDefinition { Id = 42, PermitTie = false };
        var tiers = new[] { Tier(1, 1, 1, item: 54160, grade: 12), Tier(2, 2, 5, item: 54161, grade: 11) };

        var grants = RankPayouts.Plan(board, tiers, [Place(1, 5), Place(2, 6), Place(6, 7)]);

        await Assert.That(grants.Count).IsEqualTo(2); // the sixth place is in no tier
        await Assert.That(grants[0].ItemId).IsEqualTo(54160u);
        await Assert.That(grants[0].ItemGradeId).IsEqualTo(12);
        await Assert.That(grants[0].HolderId).IsEqualTo(5UL);
        await Assert.That(grants[0].Position).IsEqualTo(1u);
        await Assert.That(grants[1].ItemId).IsEqualTo(54161u);
        await Assert.That(grants[1].HolderId).IsEqualTo(6UL);
    }

    [Test]
    public async Task Plan_LeavesABoardAloneWhenItsTiersPayNothing()
    {
        // The gear board ships tiers with no reward, which is what "nothing to pay" looks like.
        var board = new RankDefinition { Id = 23, PermitTie = false };

        var grants = RankPayouts.Plan(board, [Tier(1, 1, 1), Tier(2, 2, 5)], [Place(1, 5), Place(2, 6)]);

        await Assert.That(grants).IsEmpty();
    }
}
