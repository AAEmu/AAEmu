using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Shipyard;

public class ShipyardTickTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        ShipyardManager.Instance.ShipyardTick();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}

public class ShipyardCompleteTask : Task
{
    public Game.Shipyard.Shipyard _shipyard;

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        ShipyardManager.Instance.ShipyardCompleted(_shipyard);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
