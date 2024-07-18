using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Duels;

public class DuelStartTask : Task
{
    protected uint _challengerId;
    public DuelStartTask(uint challengerId)
    {
        _challengerId = challengerId;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        DuelManager.Instance.DuelStart(_challengerId);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
