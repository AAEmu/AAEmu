using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

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
