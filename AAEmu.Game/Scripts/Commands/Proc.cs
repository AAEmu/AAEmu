using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Procs;

namespace AAEmu.Game.Scripts.Commands
{
    public class Proc : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "proc" };
            CommandManager.Instance.Register(name, this);
        }
        
        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Proc] " + CommandManager.CommandPrefix + "test_proc <proc id>");
                return;
            }

            if (uint.TryParse(args[0], out var procId))
            {
                var proc = new ItemProc(procId);
                proc.Apply(character, true);
            }
        }

        public string GetCommandLineHelp()
        {
            return "<proc id>";
        }

        public string GetCommandHelpText()
        {
            return "Triggers a proc from item_procs";
        }
    }

}
