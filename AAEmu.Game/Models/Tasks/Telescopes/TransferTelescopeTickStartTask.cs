using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Telescopes
{
    public class TransferTelescopeTickStartTask : Task
    {
        public override void Execute()
        {
            TransferTelescopeManager.Instance.TransferTelescopeTick();
        }
    }
}
