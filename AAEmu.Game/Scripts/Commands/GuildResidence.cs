using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command to force-demolish the caller's guild Residence, since the client's own demolish button
/// does not reliably appear - there is currently no other way to test placing a fresh Residence.
/// </summary>
public class GuildResidence : ICommand
{
    public string[] CommandNames { get; set; } = ["guildresidence", "residence"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "demolish";
    }

    public string GetCommandHelpText()
    {
        return "Force-demolishes your guild's Residence (workaround for the client's missing demolish button).";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0 || args[0] != "demolish")
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var expedition = character.Expedition;
        if (expedition == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"{character.Name} is not in a guild.");
            return;
        }

        if (expedition.ResidenceHouseId == 0)
        {
            CommandManager.SendErrorText(this, messageOutput, $"{expedition.Name} has no Residence placed.");
            return;
        }

        var house = HousingManager.Instance.GetHouseById(expedition.ResidenceHouseId);
        if (house == null)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Residence house id {expedition.ResidenceHouseId} not found - clearing the stale reference.");
            expedition.ResidenceHouseId = 0;
            return;
        }

        HousingManager.Instance.Demolish(character.Connection, house, false, true);
        CommandManager.SendNormalText(this, messageOutput, $"{expedition.Name}'s Residence (house {house.Id}) demolished.");
    }
}
