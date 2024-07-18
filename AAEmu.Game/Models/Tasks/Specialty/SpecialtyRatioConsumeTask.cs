using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Specialty;

public class SpecialtyRatioConsumeTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        SpecialtyManager.Instance.ConsumeRatio();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
