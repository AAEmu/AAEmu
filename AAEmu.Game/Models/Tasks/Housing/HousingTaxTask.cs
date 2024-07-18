using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Housing;

public class HousingTaxTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        HousingManager.Instance.CheckHousingTaxes();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
