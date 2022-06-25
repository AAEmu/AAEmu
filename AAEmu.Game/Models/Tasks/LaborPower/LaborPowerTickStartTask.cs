using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.LaborPower
{
    public class LaborPowerTickStartTask : Task
    {
        private readonly ILaborPowerManager _laborPowerManager;

        public LaborPowerTickStartTask(ILaborPowerManager laborPowerManager)
        {
            _laborPowerManager = laborPowerManager;
        }

        public override void Execute()
        {
            _laborPowerManager.LaborPowerTick();
        }
    }
}
