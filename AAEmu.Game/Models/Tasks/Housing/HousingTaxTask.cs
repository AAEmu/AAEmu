using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Housing
{
    public class HousingTaxTask : Task
    {
        public override void Execute()
        {
            HousingManager.Instance.CheckHousingTaxes();
        }        
    }
}
