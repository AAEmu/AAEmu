using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Invisible : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("invisible", this);
        }

        public string GetCommandLineHelp()
        {
            return "<true||false>";
        }

        public string GetCommandHelpText()
        {
            return "Sets yourself as invisible to other players";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Invisible] " + CommandManager.CommandPrefix + "invisible <true||false>");
                return;
            }

            if (bool.TryParse(args[0], out var value))
                character.SetInvisible(value);
        }
    }
}
