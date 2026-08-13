using AAEmu.Game;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public class NpcTowerDefKillQuotaTests
{
    [Test]
    public async Task TryConsume_CreditsOncePerLife_UntilReset()
    {
        var previous = WorldIntegration.ZoneAuthority;
        WorldIntegration.ZoneAuthority = true;
        try
        {
            var npc = new Npc { TemplateId = 8410 };

            await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out var first)).IsTrue();
            await Assert.That(first).IsEqualTo(8410u);
            await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out _)).IsFalse();

            npc.ResetTowerDefKillQuotaNotification();

            await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out var second)).IsTrue();
            await Assert.That(second).IsEqualTo(8410u);
        }
        finally
        {
            WorldIntegration.ZoneAuthority = previous;
        }
    }

    [Test]
    public async Task TryConsume_SkippedWhenZoneAuthorityOff()
    {
        var previous = WorldIntegration.ZoneAuthority;
        WorldIntegration.ZoneAuthority = false;
        try
        {
            var npc = new Npc { TemplateId = 8410 };
            await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out _)).IsFalse();
        }
        finally
        {
            WorldIntegration.ZoneAuthority = previous;
        }
    }
}
