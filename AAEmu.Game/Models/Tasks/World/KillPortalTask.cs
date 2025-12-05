using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.World;

public class KillPortalTask(Portal portal) : Task
{
    public override void Execute()
    {
        portal.Delete();
    }
}
