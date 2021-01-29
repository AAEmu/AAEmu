using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Tasks
{
    public class PortalDespawnTask : Task
    {
        private GameObject portalEntrance;
        private GameObject portalExit;

        public PortalDespawnTask(Npc portalEntrance, Npc portalExit)
        {
            this.portalEntrance = portalEntrance;
            this.portalExit = portalExit;
        }

        public override void Execute()
        {
            portalEntrance.Delete();
            portalExit.Delete();
        }
    }
}
