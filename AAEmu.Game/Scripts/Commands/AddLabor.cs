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

        public string GetCommandLineHelp()
        {
            return "<amount> [targetSkill]";
        }

        public string GetCommandHelpText()
        {
            return "Add or remove <amount> of labor. If [targetskill] is provided, then target vocation skill also gains a amount of points.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Labor] /add_labor <count> [targetSkill]");
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
