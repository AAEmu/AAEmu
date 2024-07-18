using AAEmu.Game.Core.Managers.UnitManagers;

namespace AAEmu.Game.Models.Tasks.Characters;

public class CharacterDeleteTask : Task
{
    private static object _lock = new();

    public override System.Threading.Tasks.Task ExecuteAsync()
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

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
