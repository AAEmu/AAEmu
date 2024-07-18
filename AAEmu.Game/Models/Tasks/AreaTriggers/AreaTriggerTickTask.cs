using System;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.AreaTriggers;

public class AreaTriggerTickTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        AreaTriggerManager.Instance.Tick(TimeSpan.Zero);
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
