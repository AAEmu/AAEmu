using System;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.AreaTriggers
{
    public class AreaTriggerTickTask : Task
    {
        public override void Execute()
        {
            AreaTriggerManager.Instance.Tick(TimeSpan.Zero);
        }
    }
}
