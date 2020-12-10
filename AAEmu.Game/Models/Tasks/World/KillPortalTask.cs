using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.World
{
    public class KillPortalTask : Task
    {
        private readonly Portal _portal;
        
        public KillPortalTask(Portal portal)
        {
            _portal = portal;
        }

        public override void Execute()
        {
            _portal.Delete();
        }
    }
}
