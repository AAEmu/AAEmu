using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Duels;

public class DuelStartTask(uint challengerId) : Task
{
    protected uint _challengerId = challengerId;

    public override void Execute()
    {
        DuelManager.Instance.DuelStart(_challengerId);
    }
}
