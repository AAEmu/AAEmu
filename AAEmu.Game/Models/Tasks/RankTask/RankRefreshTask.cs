using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.RankTask;

/// <summary>
/// Rebuilds every ranking board: what a holder gained or spent since the last tick, and what the characters
/// in world are worth now.
/// </summary>
public class RankRefreshTask : Task
{
    public override void Execute()
    {
        RankScoreManager.Instance.Refresh(WorldManager.Instance.GetAllCharacters());
    }
}
