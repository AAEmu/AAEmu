using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;

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
            return "(target) or self <health percent>";
        }

        public string GetCommandHelpText()
        {
            return "Damages self based on health percent";
        }

        public void Execute(Character character, string[] args)
        {
				int healthpercent = int.TryParse(args[0], out healthpercent) ? healthpercent : 1;
				if(healthpercent > 0 && healthpercent < 100){
					character.ReduceCurrentHp(character, (character.MaxHp*healthpercent/100));
				}
        }
    }
}
