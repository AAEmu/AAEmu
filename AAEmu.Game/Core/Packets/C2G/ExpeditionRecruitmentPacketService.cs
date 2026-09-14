using AAEmu.Game.Core.Managers;
using AAEmu.Commons.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Packets.C2G;

internal static class ExpeditionRecruitmentPacketService
{
    public static ExpeditionRecruitmentService Get() =>
        SingletonContainer.ServiceProvider?.GetRequiredService<ExpeditionRecruitmentService>()
        ?? throw new InvalidOperationException("Expedition recruitment service is unavailable.");
}
