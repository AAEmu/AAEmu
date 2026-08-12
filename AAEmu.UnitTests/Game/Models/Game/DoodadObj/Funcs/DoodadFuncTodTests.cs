using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncTodTests
{
    [Test]
    public async Task GetClockHours_Realtime_UsesUtcWallClock()
    {
        var tod = new DoodadFuncTod { IsRealtime = true };
        var before = (float)DateTime.UtcNow.TimeOfDay.TotalHours;
        var hours = tod.GetClockHours();
        var after = (float)DateTime.UtcNow.TimeOfDay.TotalHours;

        // Allow day wrap near midnight.
        if (after + 0.001f < before)
        {
            await Assert.That(hours is >= 0f and < 24f).IsTrue();
            return;
        }

        await Assert.That(hours).IsGreaterThanOrEqualTo(before - 0.001f);
        await Assert.That(hours).IsLessThanOrEqualTo(after + 0.001f);
    }
}
