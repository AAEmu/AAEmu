using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.World;

public class KillPortalTask : Task
{
    private readonly Portal _portal;

    public KillPortalTask(Portal portal)
    {
        _portal = portal;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        _portal.Delete();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
