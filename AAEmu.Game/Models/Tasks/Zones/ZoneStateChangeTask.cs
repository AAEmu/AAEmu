using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Models.Tasks.Zones
{
    public class ZoneStateChangeTask : Task
    {
        public ZoneConflict ZoneConflict;

        public ZoneStateChangeTask(ZoneConflict zc)
        {
            ZoneConflict = zc;
        }

        public override void Execute()
        {
            // Just checking for timer should be enough to trigger the next state
            ZoneConflict.CheckTimer();
        }
    }
}
