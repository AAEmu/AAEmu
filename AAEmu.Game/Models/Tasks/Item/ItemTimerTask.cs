using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Item
{
    public class ItemTimerTask : Task
    {
        public override void Execute()
        {
            ItemManager.Instance.UpdateItemTimers();
        }        
    }
}
