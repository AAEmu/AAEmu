using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Specialty;

public sealed class SpecialtyRatioRegenTask(SpecialtyManager specialtyManager) : Task
{
    public override void Execute()
    {
        specialtyManager.RecoverTimedRatios();
    }
}
