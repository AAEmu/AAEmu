using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.PublicFarm;

public class PublicFarmTickStartTask : Task
{
    public PublicFarmTickStartTask()
    {
    }

    public override void Execute()
    {
        PublicFarmManager.Instance.PublicFarmTick();
    }
}
