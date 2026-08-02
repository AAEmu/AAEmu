using AAEmu.Game.Core.Managers.UnitManagers;

namespace AAEmu.Game.Models.Tasks.Characters;

public class CharacterDeleteTask : Task
{
    private static readonly object _lock = new();
    private static NLog.Logger Logger { get; } = NLog.LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        lock (_lock)
        {
            try
            {
                CharacterManager.Instance.CheckForDeletedCharacters();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Character deletion task failed");
            }
        }
    }
}
