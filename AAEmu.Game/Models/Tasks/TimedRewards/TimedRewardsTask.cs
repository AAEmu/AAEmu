using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.TimedRewards;

public class TimedRewardsTask : Task
{
    public TimedRewardsTask()
    {
        //
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        TimedRewardsManager.Instance.DoTick();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
