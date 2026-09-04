using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command to add guild contribution/prestige points to a guild member (self, current target, or
/// by name), via ExpeditionManager.TryChangeContributionPoints - the same path CashShop/item-purchase
/// contribution spends already use, so it persists and broadcasts the same way real gains do.
/// </summary>
public class GuildPrestige : ICommand
{
    public string[] CommandNames { get; set; } = ["guildprestige", "contribution"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <amount>";
    }

    public string GetCommandHelpText()
    {
        return "Adds guild contribution/prestige points to a guild member (self, current target, or by name).";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstArg);

        if (targetPlayer.Expedition == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"{targetPlayer.Name} is not in a guild.");
            return;
        }

        if (args.Length <= firstArg || !int.TryParse(args[firstArg], out var amount) || amount == 0)
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /guildprestige (target) <amount>");
            return;
        }

        if (!ExpeditionManager.Instance.TryChangeContributionPoints(targetPlayer, amount, amount > 0))
        {
            CommandManager.SendErrorText(this, messageOutput, "Failed to change contribution points.");
            return;
        }

        var newTotal = targetPlayer.Expedition.GetMember(targetPlayer)?.ContributionPoint ?? 0;
        CommandManager.SendNormalText(this, messageOutput, $"{targetPlayer.Name}'s guild contribution is now {newTotal}.");
    }
}
