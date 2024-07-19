using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.TimedRewards;

public class TimedRewardsTask : Task
{
    public TimedRewardsTask()
    {
        //
    }

    public override void Execute()
    {
        TimedRewardsManager.Instance.DoTick();
    }
}
