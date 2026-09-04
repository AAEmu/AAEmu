using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class GearScore : ICommand
{
    public string[] CommandNames { get; set; } = ["gearscore", "gear_score", "gs"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target)";
    }

    public string GetCommandHelpText()
    {
        return "Shows the target player's server-side gear score (sum over equipped pieces).";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var target = WorldManager.Instance.GetTargetOrSelf(character, args.Length > 0 ? args[0] : "", out var _);
        if (target is not Character player || player == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "No player target.");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"[GearScore] {player.Name}: {player.GearScore}");
    }
}
