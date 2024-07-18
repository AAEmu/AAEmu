using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.SaveTask;

public class SaveTickStartTask : Task
{
    public SaveTickStartTask()
    {
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        SaveManager.Instance.SaveTick();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
