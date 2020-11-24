using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class ClearCombat : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "clearcombat", "clear_combat", "cc" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Command to clear combat.";
        }

        public void Execute(Character character, string[] args)
        {
            character.SendPacket(new SCCombatClearedPacket(character.ObjId));
        }
    }
}
