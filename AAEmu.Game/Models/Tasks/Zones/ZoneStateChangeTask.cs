using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Tasks.Zones;

public class ZoneStateChangeTask : Task
{
    public ZoneConflict ZoneConflict;

    public ZoneStateChangeTask(ZoneConflict zc)
    {
        ZoneConflict = zc;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        // Just checking for timer should be enough to trigger the next state
        ZoneConflict.CheckTimer();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
