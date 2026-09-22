using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Models.Game;

public class TelescopeRegistrationEntryTests
{
    [Test]
    public async Task BossTelescopeUsesTheLargestRemainingBuffRange()
    {
        var entry = new TelescopeRegistrationEntry();

        entry.SetBossTelescopeRange(10, 800f);
        entry.SetBossTelescopeRange(11, 1200f);
        entry.SetBossTelescopeRange(11, 0f);

        await Assert.That(entry.ShowBossTelescopeRange).IsEqualTo(800f);
        await Assert.That(entry.IsActive).IsTrue();

        entry.SetBossTelescopeRange(10, 0f);
        await Assert.That(entry.ShowBossTelescopeRange).IsEqualTo(0f);
        await Assert.That(entry.IsActive).IsFalse();
    }
}
