using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SeatTopologyTests
{
    [Test]
    public async Task Mate_StartsWithNoImplicitSeats()
    {
        var mate = new Mate();

        await Assert.That(mate.Passengers).IsEmpty();
    }

    [Test]
    public async Task Mate_InitializesOnlyTheExplicitMultiSeatSet()
    {
        var mate = new Mate();
        mate.InitializeSeatTopology([
            AttachPointKind.Passenger1,
            AttachPointKind.Driver,
            AttachPointKind.Cannon0,
            AttachPointKind.Passenger1,
        ]);

        await Assert.That(mate.Passengers.Keys).IsEquivalentTo(new[]
        {
            AttachPointKind.Driver,
            AttachPointKind.Passenger1,
        });
    }

    [Test]
    public async Task Slave_InitializesOnlyTheExplicitOneSeatSet()
    {
        var slave = new Slave();
        slave.InitializeSeatTopology([AttachPointKind.Driver]);

        await Assert.That(slave.HasSeat(AttachPointKind.Driver)).IsTrue();
        await Assert.That(slave.HasSeat(AttachPointKind.Passenger0)).IsFalse();
    }

    [Test]
    public async Task Slave_RejectsAnAttachPointNotInTheJoinBackedSet()
    {
        var slave = new Slave();
        slave.InitializeSeatTopology([AttachPointKind.Passenger0]);

        await Assert.That(slave.HasSeat(AttachPointKind.Passenger0)).IsTrue();
        await Assert.That(slave.HasSeat(AttachPointKind.Driver)).IsFalse();
        await Assert.That(slave.HasSeat(AttachPointKind.Cannon0)).IsFalse();
    }

    [Test]
    public async Task DeathCleanup_ReleasesEveryRiderFromAStableSnapshot()
    {
        var riders = new Dictionary<AttachPointKind, object>
        {
            [AttachPointKind.Driver] = new object(),
            [AttachPointKind.Passenger0] = new object(),
        };
        var released = new List<AttachPointKind>();

        var snapshot = SeatTopologyRules.ReleaseAllRiders(
            riders,
            (seat, _) =>
            {
                released.Add(seat);
                riders.Remove(seat);
            });

        await Assert.That(snapshot).IsEquivalentTo(new[]
        {
            AttachPointKind.Driver,
            AttachPointKind.Passenger0,
        });
        await Assert.That(released).IsEquivalentTo(snapshot);
        await Assert.That(riders).IsEmpty();
    }
}
