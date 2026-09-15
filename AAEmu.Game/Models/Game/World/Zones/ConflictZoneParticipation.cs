using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Entry points for conflict-zone participation from the game models (NPC death, quest completion).
/// </summary>
/// <remarks>
/// The zone manager is resolved through <see cref="SingletonContainer"/> instead of the static
/// <c>ZoneManager.Instance</c> accessor because these primitives are also exercised by unit tests
/// that run without a DI container, and <c>ZoneManager</c> has no parameterless constructor. With no
/// container there is no live zone data, so participation is simply skipped. The dedicated World
/// process also has no container; its NPC deaths are World-only and must not be counted here.
/// </remarks>
public static class ConflictZoneParticipation
{
    public static void RegisterNpcKill(Npc npc) =>
        SingletonContainer.ServiceProvider?.GetService<IZoneManager>()?.RegisterNpcKill(npc);

    public static void RegisterQuestCompletion(Character character, uint questId) =>
        SingletonContainer.ServiceProvider?.GetService<IZoneManager>()?.RegisterQuestCompletion(character, questId);
}
