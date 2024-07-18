using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.PublicFarm;

public class PublicFarmTickStartTask : Task
{
    public PublicFarmTickStartTask()
    {
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        PublicFarmManager.Instance.PublicFarmTick();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
