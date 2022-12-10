using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Shipyard
{
    public class ShipyardTickTask : Task
    {
        public override void Execute()
        {
            ShipyardManager.Instance.ShipyardTick();
        }
    }
    
    public class ShipyardCompleteTask : Task
    {
        public Game.Shipyard.Shipyard _shipyard;

        public override void Execute()
        {
            ShipyardManager.Instance.ShipyardCompleted(_shipyard);
        }
    }
}
