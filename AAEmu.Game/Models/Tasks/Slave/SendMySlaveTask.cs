using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Tasks.Slave;

public class SendMySlaveTask(WorldInstance world) : Task
{
    public override void Execute()
    {
        world.SlaveManager.SendMySlavePacketToAllOwners();
    }
}
