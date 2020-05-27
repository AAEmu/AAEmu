using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Specialty
{
    public class SpecialtyRatioRegenTask : Task
    {
        public override void Execute()
        {
            SpecialtyManager.Instance.RegenRatio();
        }
    }
}
