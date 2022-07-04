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
            return "(target) or self <damage> [%]";
        }

        public string GetCommandHelpText()
        {
            return "Damages self or target based on raw damage or Hp percentage" +
                   "Usage: /damage 9999   - Inflicts 9999 damage" +
                   "or alternatively: /damage 80 %   - Inflicts 80% hp damage" +
                   "Note: the percentage must be between 1 and 100";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Damage] " + CommandManager.CommandPrefix + "damage (self or target) <damage> [%]");
                return;
            }

			if (!int.TryParse(args[0], out int damage))
				damage = 1;
				
			if ((args.Length == 1) && (damage > 0))
			{
				character.ReduceCurrentHp(character, damage);
			}

            if ((args.Length == 2) && (args[1] == "%") && (damage > 0 && damage <= 100))
            {
               character.ReduceCurrentHp(character, (character.MaxHp * damage / 100));
            }
        }
               
    }
}
