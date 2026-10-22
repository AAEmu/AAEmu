using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.World;

/// <summary>
/// Expires a temporary worldgate. Deleting the entrance cascades to its linked exit
/// via <see cref="Portal.Delete"/>.
/// </summary>
public class KillPortalTask(Portal portal) : Task
{
    public override void Execute()
    {
        portal.Delete();
    }
}
