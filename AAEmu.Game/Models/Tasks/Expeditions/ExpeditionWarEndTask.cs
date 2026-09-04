using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Tasks.Expeditions;

/// <summary>Fires when a guild war's scheduled duration runs out - see ExpeditionManager.EndWar.</summary>
public class ExpeditionWarEndTask(FactionsEnum expeditionId) : Task
{
    public override void Execute()
    {
        ExpeditionManager.Instance.EndWar(expeditionId);
    }
}
