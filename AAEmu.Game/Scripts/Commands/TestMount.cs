using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestMount : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("test_mount", this);
        }

        public void Execute(Character character, string[] args)
        {
            
        }
    }
}
