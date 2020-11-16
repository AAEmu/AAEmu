using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Scripts.Commands
{
    public class Damage : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "damage" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) or self";
        }

        public string GetCommandHelpText()
        {
            return "Damages self";
        }

        public void Execute(Character character, string[] args)
        {
            character.ReduceCurrentHp(character, 100);
        }
    }
}
