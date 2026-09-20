using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddHonor : ICommand
{
    public string[] CommandNames { get; set; } = ["honorpoint", "honor", "honorpoints", "add_hp"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <HonorPoints>";
    }

    public string GetCommandHelpText()
    {
        return "Adds HonorPoints (to target player). The amount is exact - no gain-rate modifiers apply.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstArg);

        if (!int.TryParse(args[firstArg], out var honorToAdd))
        {
            honorToAdd = 0;
        }

        if (honorToAdd != 0)
        {
            targetPlayer.ChangeGamePoints(GamePointKind.Honor, honorToAdd, false);
            CommandManager.SendNormalText(this, messageOutput,
                $"[Honor] {targetPlayer.Name} honor points: {honorToAdd:+#;-#;0} -> {targetPlayer.HonorPoint}");
        }
    }
}
