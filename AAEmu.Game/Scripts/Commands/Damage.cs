using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

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
            return "<damage> [%]";
        }

        public string GetCommandHelpText()
        {
            return "Damages self or your current target based on raw damage or Hp percentage, usage:\n" +
                   "/damage 9999  -> Inflicts 9999 damage\n" +
                   "/damage 80 %  -> Inflicts 80% of target's max hp damage\n" +
                   "/damage 20%   -> Inflicts 20% of target's max hp damage";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 1)
            {
                character.SendMessage("[Damage] " + CommandManager.CommandPrefix + "damage (self or target) <damage> [%]");
                return;
            }
            
            Unit target = null;
            if (character.CurrentTarget is Unit curTarget)
                target = curTarget;
            else
                target = character;
            
            var damageStr = args[0];
            bool isPercent = false;
            
            // check if user added the % directly after the number, if so, trim it and set it as percent value
            if (damageStr.EndsWith("%"))
            {
                damageStr = damageStr.TrimEnd('%');
                isPercent = true;
            }
            // Check if the 2nd argument is a "%"
            if ((args.Length > 1) && (args[1] == "%"))
                isPercent = true;
            
            // Try to parse damage
            if (!int.TryParse(damageStr, out var damage))
                damage = 100;

            // If % based, calculate it's damage
            if (isPercent)
                damage = (target.MaxHp * damage / 100);

            // Only apply if damage > 0 and we have a valid target
            if ((damage > 0) && (target != null))
                target.ReduceCurrentHp(character, damage);
        }
    }
}
