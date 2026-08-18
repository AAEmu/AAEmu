using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public class NpcTowerDefKillQuotaTests
{
    [Test]
    public async Task TryConsume_CreditsOncePerLife_UntilReset()
    {
        var npc = new Npc { TemplateId = 8410 };

        await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out var first)).IsTrue();
        await Assert.That(first).IsEqualTo(8410u);
        await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out _)).IsFalse();

        npc.ResetTowerDefKillQuotaNotification();

        await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out var second)).IsTrue();
        await Assert.That(second).IsEqualTo(8410u);
    }

    [Test]
    public async Task TryConsume_SkippedWhenTemplateMissing()
    {
        var npc = new Npc { TemplateId = 0 };
        await Assert.That(npc.TryConsumeTowerDefKillQuotaNotification(out _)).IsFalse();
    }

    [Test]
    public async Task LuscaGrade_UsesPriorityStream()
    {
        await Assert.That(Npc.UsesPriorityStreamAsKillQuotaBoss(NpcGradeType.BossC)).IsTrue();
        await Assert.That(Npc.UsesPriorityStreamAsKillQuotaBoss(NpcGradeType.Normal)).IsFalse();
        await Assert.That(Npc.UsesPriorityStreamAsKillQuotaBoss(NpcGradeType.Elite)).IsFalse();
    }
}
