using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

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
            return "Repeats the provided arguments in chat as raw text.";
        }

        public void Execute(Character character, string[] args)
        {
            // Un-escape the string, as the client sends it escaped
            // It is required if you want to test things like @NPC_NAME() and |cFF00FFFF text colors |r
            // s = s.Replace("@@", "@").Replace("||", "|");

            character.SendMessage($"|cFFFFFFFF[Echo]|r {string.Join(' ', args)}");
        }
    }
}
