using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ActabilityCmd : ICommand
{
    public string[] CommandNames { get; set; } = ["actability", "vocation"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <id|name> <points> [step]";
    }

    public string GetCommandHelpText()
    {
        return
            "Set a vocation (actability) to <points>, optionally moving its expert step first.\n" +
            "Fishing is 7. Step 0 is Amateur (cap 10000). Rank buttons keep the point total;\n" +
            "this command still clamps to the selected step's cap.\n" +
            "Example: /actability 7 10000   or   /actability fishing 50000 4";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstArg);
        if (args.Length <= firstArg + 1)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (!TryResolveActabilityId(args[firstArg], out var actabilityId))
        {
            CommandManager.SendErrorText(this, messageOutput, $"Unknown actability '{args[firstArg]}'");
            return;
        }

        if (!int.TryParse(args[firstArg + 1], out var points))
        {
            CommandManager.SendErrorText(this, messageOutput, "points must be an integer");
            return;
        }

        int? step = null;
        if (args.Length > firstArg + 2)
        {
            if (!int.TryParse(args[firstArg + 2], out var parsedStep) || parsedStep < 0)
            {
                CommandManager.SendErrorText(this, messageOutput, "step must be a non-negative integer");
                return;
            }

            step = parsedStep;
        }

        if (!targetPlayer.Actability.TrySet(actabilityId, points, step))
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"Could not set actability {actabilityId} (missing group or expert step)");
            return;
        }

        var updated = targetPlayer.Actability.Actabilities[actabilityId];
        CommandManager.SendNormalText(this, messageOutput,
            $"{targetPlayer.Name} actability {actabilityId} = {updated.Point} (step {updated.Step})");
        if (character.Id != targetPlayer.Id)
            targetPlayer.SendMessage($"[GM] {character.Name} set your actability {actabilityId} to {updated.Point}");
    }

    private static bool TryResolveActabilityId(string raw, out uint id)
    {
        if (uint.TryParse(raw, out id))
            return id > 0;

        if (Enum.TryParse<ActabilityType>(raw, ignoreCase: true, out var named) && named != ActabilityType.None)
        {
            id = (uint)named;
            return true;
        }

        id = 0;
        return false;
    }
}
