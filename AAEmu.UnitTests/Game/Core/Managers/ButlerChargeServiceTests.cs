using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ButlerChargeServiceTests
{
    [Test]
    public async Task DailyLaborCounter_PreservesOnlyTheCurrentUtcDay()
    {
        var today = new DateTime(2026, 9, 12, 18, 30, 0, DateTimeKind.Utc);
        var butler = new CharacterButler(10);
        butler.Apply(new CharacterButlerRecord(
            10, 20, "Mira", 0, 640, 0, Helpers.UnixTime(today.Date)));

        var valid = ButlerChargeService.TryResolveDailyLaborCounter(
            butler, today, out var chargedAmount, out var currentPeriod);

        await Assert.That(valid).IsTrue();
        await Assert.That(chargedAmount).IsEqualTo((ushort)640);
        await Assert.That(currentPeriod).IsEqualTo(Helpers.UnixTime(today.Date));
    }

    [Test]
    public async Task DailyLaborCounter_ResetsAnOlderPersistedDay()
    {
        var today = new DateTime(2026, 9, 12, 18, 30, 0, DateTimeKind.Utc);
        var butler = new CharacterButler(10);
        butler.Apply(new CharacterButlerRecord(
            10, 20, "Mira", 0, 640, 0, Helpers.UnixTime(today.Date.AddDays(-1))));

        var valid = ButlerChargeService.TryResolveDailyLaborCounter(
            butler, today, out var chargedAmount, out var currentPeriod);

        await Assert.That(valid).IsTrue();
        await Assert.That(chargedAmount).IsEqualTo((ushort)0);
        await Assert.That(currentPeriod).IsEqualTo(Helpers.UnixTime(today.Date));
    }

    [Test]
    public async Task DailyLaborCounter_RejectsAFuturePersistedDay()
    {
        var today = new DateTime(2026, 9, 12, 18, 30, 0, DateTimeKind.Utc);
        var butler = new CharacterButler(10);
        butler.Apply(new CharacterButlerRecord(
            10, 20, "Mira", 0, 640, 0, Helpers.UnixTime(today.Date.AddDays(1))));

        var valid = ButlerChargeService.TryResolveDailyLaborCounter(
            butler, today, out _, out _);

        await Assert.That(valid).IsFalse();
    }
}
