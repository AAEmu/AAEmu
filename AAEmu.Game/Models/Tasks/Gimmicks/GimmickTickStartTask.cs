using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Gimmicks;

public class GimmickTickStartTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        GimmickManager.Instance.GimmickTick();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
