using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers.UnitManagers;

namespace AAEmu.Game.Models.Tasks.Characters
{
    class CharacterDeleteTask : Task
    {
        private static object _lock = new object();
        private static bool isChecking = false;
        public static bool enabled { get; set; } = false;
        
        public override void Execute()
        {
            if ((!isChecking) && (enabled))
            {
                lock (_lock)
                {
                    try
                    {
                        isChecking = true;
                        CharacterManager.Instance.CheckForDeletedCharactersTick();
                        isChecking = false;
                    }
                    catch (Exception e)
                    {
                        // Do nothing
                    }
                }
            }
        }
    }
}
