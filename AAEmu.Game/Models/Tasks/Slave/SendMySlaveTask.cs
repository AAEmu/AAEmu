using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Slave
{
    public class SendMySlaveTask : Task
    {
        public override void Execute()
        {
            SlaveManager.Instance.SendMySlavePacketToAllOwners();
        }
    }
}
