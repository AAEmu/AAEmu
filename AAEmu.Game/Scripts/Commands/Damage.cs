using AAEmu.Game.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

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

        public void Execute(ICharacter character, string[] args)
        {
            character.ReduceCurrentHp(character, 100);
        }
    }
}
