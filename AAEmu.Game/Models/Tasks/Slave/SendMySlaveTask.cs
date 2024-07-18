using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Slave;

public class SendMySlaveTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        SlaveManager.Instance.SendMySlavePacketToAllOwners();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
