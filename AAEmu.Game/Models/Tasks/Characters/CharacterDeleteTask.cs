using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers.UnitManagers;

namespace AAEmu.Game.Models.Tasks.Characters
{
    class CharacterDeleteTask : Task
    {
        private static object _lock = new object();

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
}
