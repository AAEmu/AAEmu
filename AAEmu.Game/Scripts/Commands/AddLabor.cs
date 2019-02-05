using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddLabor : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("add_labor", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Labor] /add_labor <count> <actability id?>");
                return;
            }

            var count = short.Parse(args[0]);
            var actability = 0;
            if (args.Length > 1)
                actability = int.Parse(args[1]);
            
            character.ChangeLabor(count, actability);
        }
    }
}
