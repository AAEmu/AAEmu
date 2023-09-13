using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Transfers
{
    public class TransferTickStartTask : Task
    {
        public override void Execute()
        {
            TransferManager.Instance.TransferTick();
        }
    }
}
