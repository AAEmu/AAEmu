using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.LaborPower
{
    public class LaborPowerTickStartTask : Task
    {
        public LaborPowerTickStartTask()
        {
        }

        public override void Execute()
        {
            LaborPowerManager.Instance.LaborPowerTick();
        }
    }
}
