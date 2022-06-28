using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class GodMode : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("godmode", this);
        }

        public string GetCommandLineHelp()
        {
            return "<true||false>";
        }

        public string GetCommandHelpText()
        {
            return "Makes himself immortal to other players";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage( "[GodMode] " + CommandManager.CommandPrefix + "godmode <true||false>" );
                return;
            }

            if (bool.TryParse(args[0], out var value))
                character.SetGodMode(value);
        }
    }
}
