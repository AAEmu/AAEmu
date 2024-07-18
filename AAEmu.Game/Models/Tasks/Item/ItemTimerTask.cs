using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Item;

public class ItemTimerTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        ItemManager.Instance.UpdateItemTimers();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
