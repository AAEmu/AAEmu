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

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Invisible] /invisible <true/false>");
                return;
            }

            if (bool.TryParse(args[0], out var value))
                character.SetInvisible(value);
        }
    }
}