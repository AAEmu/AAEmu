using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Damage : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "damage" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<damage> [%]";
    }

    public string GetCommandHelpText()
    {
        return $"Damages self or your current target based on raw damage or Hp percentage, usage:\n" +
               $"{CommandManager.CommandPrefix}{CommandNames[0]} 9999  -> Inflicts 9999 damage\n" +
               $"{CommandManager.CommandPrefix}{CommandNames[0]} 80 %  -> Inflicts 80% of target's max hp damage\n" +
               $"{CommandManager.CommandPrefix}{CommandNames[0]} 20%   -> Inflicts 20% of target's max hp damage";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        Unit target = null;
        if (character.CurrentTarget is Unit curTarget)
        {
            target = curTarget;
        }
        else
        {
            target = character;
        }

        var damageStr = args[0];
        var isPercent = false;

        // check if user added the % directly after the number, if so, trim it and set it as percent value
        if (damageStr.EndsWith("%"))
        {
            damageStr = damageStr.TrimEnd('%');
            isPercent = true;
        }

        // Check if the 2nd argument is a "%"
        if (args.Length > 1 && args[1] == "%")
        {
            isPercent = true;
        }

        // Try to parse damage
        if (!int.TryParse(damageStr, out var damage))
        {
            damage = 100;
        }

        // If % based, calculate its damage
        if (isPercent)
        {
            damage = target.MaxHp * damage / 100;
        }

        // Only apply if damage > 0 and we have a valid target
        if (damage > 0)
        {
            target.ReduceCurrentHp(character, damage);
        }
    }
}
