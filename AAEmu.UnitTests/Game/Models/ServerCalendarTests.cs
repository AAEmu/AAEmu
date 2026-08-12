using AAEmu.Game.Models;

namespace AAEmu.UnitTests.Game.Models;

public class ServerCalendarTests
{
    [Test]
    public async Task AsUtc_PreservesUtcKind()
    {
        var utc = new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Utc);
        await Assert.That(ServerCalendar.AsUtc(utc)).IsEqualTo(utc);
        await Assert.That(ServerCalendar.AsUtc(utc).Kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Test]
    public async Task AsUtc_Unspecified_IsTreatedAsAlreadyUtc()
    {
        // MySQL leave_time often arrives Unspecified even when the stored value is UTC.
        var unspecified = new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Unspecified);
        var asUtc = ServerCalendar.AsUtc(unspecified);
        await Assert.That(asUtc).IsEqualTo(new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Utc));
        await Assert.That(asUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        // Must NOT shift by local offset the way ToUniversalTime() would.
        await Assert.That(asUtc.Hour).IsEqualTo(23);
    }

    [Test]
    public async Task WeekStartMondayContaining_UsesUtcDate()
    {
        // Sunday 2026-08-09 01:00 Unspecified → still Sunday UTC → week starts Monday 2026-08-03.
        var sunday = new DateTime(2026, 8, 9, 1, 0, 0, DateTimeKind.Unspecified);
        await Assert.That(ServerCalendar.WeekStartMondayContaining(sunday))
            .IsEqualTo(new DateTime(2026, 8, 3));
    }

    [Test]
    public async Task AsUtc_Local_ConvertsToUtc()
    {
        var local = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Local);
        var asUtc = ServerCalendar.AsUtc(local);
        await Assert.That(asUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(asUtc).IsEqualTo(local.ToUniversalTime());
    }
}
