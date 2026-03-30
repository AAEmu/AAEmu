using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Crime;

public class TrialUpdateTask : Task
{
    private static readonly object _lock = new();

    public override void Execute()
    {
        lock (_lock)
        {
            try
            {
                TrialManager.Instance.UpdateTick();
            }
            catch
            {
                // Do nothing
            }
        }
    }
}
