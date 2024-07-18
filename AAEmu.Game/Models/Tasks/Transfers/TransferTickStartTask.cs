using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Transfers;

public class TransferTickStartTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        TransferManager.Instance.TransferTick();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
