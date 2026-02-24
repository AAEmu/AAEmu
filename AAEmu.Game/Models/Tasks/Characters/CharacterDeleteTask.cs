using AAEmu.Game.Core.Managers.UnitManagers;

namespace AAEmu.Game.Models.Tasks.Characters;

public class CharacterDeleteTask : Task
{
    private static readonly object _lock = new();

    public override void Execute()
    {
        lock (_lock)
        {
            try
            {
                CharacterManager.Instance.CheckForDeletedCharacters();
            }
            catch
            {
                // Do nothing
            }
        }
    }
}
