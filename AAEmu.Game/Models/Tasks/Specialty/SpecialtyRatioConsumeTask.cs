using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Specialty
{
    public class SpecialtyRatioConsumeTask : Task
    {
        public override void Execute()
        {
            SpecialtyManager.Instance.ConsumeRatio();
        }
    }
}
