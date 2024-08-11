using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddXP : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "xp", "add_xp", "addxp", "givexp" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <xp>";
    }

    public string GetCommandHelpText()
    {
        return "Adds experience points (to target player)";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);

        var xpToAdd = 0;
        if (int.TryParse(args[firstArg + 0], out var parseXp))
        {
            xpToAdd = parseXp;
        }

        if (xpToAdd > 0)
        {
            targetPlayer.AddExp(xpToAdd, true);
        }
    }
}
