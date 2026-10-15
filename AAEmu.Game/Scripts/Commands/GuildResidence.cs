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
        return "<info|demolish|push>";
    }

    public string GetCommandHelpText()
    {
        return "Guild Residence helpers: info (association), push (re-send SCHouseData + tax so the client learns the TlId), demolish (force-remove; client demolish button is unreliable).";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
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

        switch (args[0].ToLowerInvariant())
        {
            case "info":
                ShowInfo(character, expedition, messageOutput);
                return;
            case "push":
                HousingManager.Instance.SendExpeditionHouseInfo(character);
                CommandManager.SendNormalText(this, messageOutput,
                    expedition.ResidenceHouseId == 0
                        ? $"{expedition.Name} has no Residence to push."
                        : $"Pushed Residence tax info for house {expedition.ResidenceHouseId}.");
                return;
            case "demolish":
                DemolishResidence(character, expedition, messageOutput);
                return;
            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                return;
        }
    }

    private void ShowInfo(Character character, Models.Game.Expeditions.Expedition expedition, IMessageOutput messageOutput)
    {
        if (expedition.ResidenceHouseId == 0)
        {
            CommandManager.SendNormalText(this, messageOutput, $"{expedition.Name}: no Residence associated (residence_house_id=0).");
            return;
        }

        var house = HousingManager.Instance.GetHouseById(expedition.ResidenceHouseId);
        if (house == null)
        {
            CommandManager.SendErrorText(this, messageOutput,
                $"{expedition.Name}: residence_house_id={expedition.ResidenceHouseId} but no loaded house (stale).");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"{expedition.Name}: houseId={house.Id} tl={house.TlId} template={house.TemplateId} owner={house.OwnerId} at ({house.Transform.World.Position.X:0.#},{house.Transform.World.Position.Y:0.#},{house.Transform.World.Position.Z:0.#}) zone={house.Transform.ZoneId}");
    }

    private void DemolishResidence(Character character, Models.Game.Expeditions.Expedition expedition, IMessageOutput messageOutput)
    {
        if (expedition.ResidenceHouseId == 0)
        {
            CommandManager.SendErrorText(this, messageOutput, $"{expedition.Name} has no Residence placed.");
            return;
        }

        var house = HousingManager.Instance.GetHouseById(expedition.ResidenceHouseId);
        if (house == null)
        {
            var staleId = expedition.ResidenceHouseId;
            if (!ExpeditionManager.Instance.TrySetResidenceHouseId(expedition, staleId, 0))
            {
                CommandManager.SendErrorText(this, messageOutput, $"Residence house id {staleId} not found and could not clear the stale reference.");
                return;
            }

            CommandManager.SendErrorText(this, messageOutput, $"Residence house id {staleId} not found - cleared the stale reference.");
            return;
        }

        HousingManager.Instance.Demolish(character.Connection, house, false, true);
        CommandManager.SendNormalText(this, messageOutput, $"{expedition.Name}'s Residence (house {house.Id}) demolished.");
    }
}
