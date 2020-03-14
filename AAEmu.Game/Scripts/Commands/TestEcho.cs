using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestEcho : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("echo", this);
        }

        public string GetCommandLineHelp()
        {
            return "<text>";
        }

        public string GetCommandHelpText()
        {
            return "Repeats the provided arguments in chat";
        }

        public void Execute(Character character, string[] args)
        {
            string s = string.Empty;
            foreach (string a in args)
                s = s + a + " ";
            character.SendMessage("|cFFFFFFFF[Echo]|r " + s);
        }
    }
}
