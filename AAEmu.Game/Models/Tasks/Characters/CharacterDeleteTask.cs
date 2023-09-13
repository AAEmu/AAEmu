using AAEmu.Game.Core.Managers.UnitManagers;

namespace AAEmu.Game.Models.Tasks.Characters
{
    class CharacterDeleteTask : Task
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
    }
}