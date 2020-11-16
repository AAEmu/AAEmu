using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class ResetSkillCooldowns : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "resetcd", "resetskillcooldowns", "rcd"};
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Resets skill cooldowns.";
        }

        public void Execute(Character character, string[] args)
        {
            character.ResetAllSkillCooldowns(false);
        }
    }
}
