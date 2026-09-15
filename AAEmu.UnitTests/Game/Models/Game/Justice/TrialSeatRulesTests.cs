using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

/// <summary>
/// The bench table a trial teleports jurors to: both courts, both banks, five seats each. A seat the
/// table does not know must answer null so the caller refuses instead of placing a juror at a guess.
/// </summary>
public class TrialSeatRulesTests
{
    [Test]
    public async Task GetSeat_CoversBothCourtsAndBothBanks()
    {
        for (var court = 0; court <= 1; court++)
        {
            for (var seat = 0; seat < TrialSeatRules.SeatsPerBank; seat++)
            {
                var west = TrialSeatRules.GetSeat(court, true, seat);
                var east = TrialSeatRules.GetSeat(court, false, seat);

                await Assert.That(west).IsNotNull();
                await Assert.That(east).IsNotNull();
                await Assert.That(west!.Value.X).IsGreaterThan(0f);
                await Assert.That(east!.Value.Z).IsGreaterThan(0f);
            }
        }
    }

    [Test]
    public async Task GetSeat_RejectsSeatsOutsideTheBench()
    {
        await Assert.That(TrialSeatRules.GetSeat(0, true, TrialSeatRules.SeatsPerBank)).IsNull();
        await Assert.That(TrialSeatRules.GetSeat(0, false, -1)).IsNull();
        await Assert.That(TrialSeatRules.GetSeat(2, true, 0)).IsNull();
    }

    [Test]
    public async Task CourtForNation_MapsTheAlliancesToTheirCourtroom()
    {
        await Assert.That(TrialSeatRules.CourtForNation(true)).IsEqualTo(0);
        await Assert.That(TrialSeatRules.CourtForNation(false)).IsEqualTo(1);
    }

    [Test]
    public async Task GetGroundBench_ReportsTheFloorBankWithItsOwnChair()
    {
        // Each court is one floor row and one raised gallery. The client draws the juror in the bank
        // it is told, so a chair from the floor row must travel with the floor row's own flag - the
        // two courts do not even agree on which bank that is.
        for (var court = 0; court <= 1; court++)
        {
            for (var seat = 0; seat < TrialSeatRules.SeatsPerBank; seat++)
            {
                var bench = TrialSeatRules.GetGroundBench(court, seat);
                await Assert.That(bench).IsNotNull();

                var west = TrialSeatRules.GetSeat(court, true, seat)!.Value;
                var east = TrialSeatRules.GetSeat(court, false, seat)!.Value;

                var expectedWest = west.Z <= east.Z;
                await Assert.That(bench!.Value.IsWest).IsEqualTo(expectedWest);
                await Assert.That(bench.Value.Position.Z)
                    .IsEqualTo(expectedWest ? west.Z : east.Z);
                await Assert.That(bench.Value.JuryNumber).IsEqualTo(seat);
                await Assert.That(bench.Value.Court).IsEqualTo(court);
            }
        }

        // Marianople hears its case on the west row, the eastern court on the east row.
        await Assert.That(TrialSeatRules.GetGroundBench(0, 0)!.Value.IsWest).IsTrue();
        await Assert.That(TrialSeatRules.GetGroundBench(1, 0)!.Value.IsWest).IsFalse();
    }

    [Test]
    public async Task GetGroundBench_RejectsSeatsOutsideTheBench()
    {
        await Assert.That(TrialSeatRules.GetGroundBench(2, 0)).IsNull();
        await Assert.That(TrialSeatRules.GetGroundBench(0, TrialSeatRules.SeatsPerBank)).IsNull();
    }
}
