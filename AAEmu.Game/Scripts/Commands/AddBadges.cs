using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddBadges : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "vocation", "vocationpoints", "add_vp", "add_vb" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <VocationPoints>";
    }

    public string GetCommandHelpText()
    {
        return "Adds VocationPoints (to target player)";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);

        if (!int.TryParse(args[firstArg], out var vpToAdd))
        {
            vpToAdd = 0;
        }

        if (vpToAdd != 0)
        {
            targetPlayer.ChangeGamePoints((GamePointKind)1, vpToAdd);
        }
    }
}
