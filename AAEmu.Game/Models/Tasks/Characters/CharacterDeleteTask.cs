using AAEmu.Game.Core.Managers.UnitManagers;

namespace AAEmu.Game.Models.Tasks.Characters;

public class CharacterDeleteTask : Task
{
    private static object _lock = new();

    public override void Execute()
    {
        lock (_lock)
        {
            try
            {
                CharacterManager.CheckForDeletedCharacters();
            }
            catch
            {
                // Do nothing
            }
        }
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        Execute();
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
