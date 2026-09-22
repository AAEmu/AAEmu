using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Factions;

/// <summary>Ends hero agreements whose term is over and drops unanswered requests; re-armed by the manager for the next due time.</summary>
public class FactionDiplomacySweepTask : Task
{
    public override void Execute()
    {
        FactionDiplomacyManager.Instance.Sweep(DateTime.UtcNow);
    }
}
