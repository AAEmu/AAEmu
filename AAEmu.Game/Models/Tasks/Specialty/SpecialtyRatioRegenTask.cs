using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Specialty;

public class SpecialtyRatioRegenTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        SpecialtyManager.Instance.RegenRatio();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
