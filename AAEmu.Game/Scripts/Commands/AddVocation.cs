using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddVocation : ICommand
{
    public string[] CommandNames { get; set; } = ["vocation", "vocationpoints", "livingpoints", "add_vp", "add_vb"];

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
        return "Adds VocationPoints (to target player). The amount is exact - no gain-rate or +15% living bonus applies.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstArg);

        if (args.Length <= firstArg || !int.TryParse(args[firstArg], out var vocationToAdd))
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (vocationToAdd != 0)
        {
            targetPlayer.ChangeGamePoints(GamePointKind.Vocation, vocationToAdd, false);
            CommandManager.SendNormalText(this, messageOutput,
                $"[Vocation] {targetPlayer.Name} vocation points: {vocationToAdd:+#;-#;0} -> {targetPlayer.VocationPoint}");
        }
    }
}
