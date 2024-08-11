using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class CofferActions : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "coffer", "chest" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<action> [doodadObjId]";
    }

    public string GetCommandHelpText()
    {
        return "View or manipulate a coffer doodad, using one of the following actions:\n" +
               "close - Force-Closes a coffer so it can be opened again by other people.\n" +
               "view - View the coffer contents\n" +
               "\n" +
               "If doodadObjId is omitted, the first found coffer in a 4m radius will be used.\n";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var checkRadius = 4f;
        var action = args.Length >= 1 ? args[0].ToLower() : "help";
        var doodadObjIdStr = args.Length >= 2 ? args[1] : "0";

        if (action == "help")
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (!uint.TryParse(doodadObjIdStr, out var doodadObjId))
        {
            CommandManager.SendErrorText(this, messageOutput, "Error parsing doodadObjId !");
            return;
        }

        DoodadCoffer coffer = null;
        if (doodadObjId <= 0)
        {
            var doodads = WorldManager.GetAround<DoodadCoffer>(character, checkRadius);
            if (doodads.Count <= 0)
            {
                CommandManager.SendErrorText(this, messageOutput, "No coffers found nearby !");
                return;
            }

            coffer = doodads.First();
        }
        else
        {
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            if (doodad is DoodadCoffer dCoffer)
            {
                coffer = dCoffer;
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput, $"No coffers found with objId: {doodadObjId} !");
                return;
            }
        }

        if (coffer == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "Not sure how we got here !?");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"objId: {coffer.ObjId}, DoodadDbId: {coffer.DbId}, ItemContainerDbId: {coffer.ItemContainer.ContainerId}, UsedBy: {coffer.OpenedBy?.Name ?? "<nobody>"}");

        switch (action)
        {
            case "view":
                foreach (var item in coffer.ItemContainer.Items)
                {
                    var slotName = item.Slot.ToString();
                    var countName = "|ng;" + item.Count.ToString() + "|r x ";
                    if (item.Count == 1)
                    {
                        countName = string.Empty;
                    }

                    character.SendMessage(
                        $"[|nd;@DOODAD_NAME({coffer.TemplateId})|r][{slotName}] |nb;{item.Id}|r {countName}|nn;{item.TemplateId}|r = @ITEM_NAME({item.TemplateId})");
                }

                CommandManager.SendNormalText(this, messageOutput,
                    $"[|nd;@DOODAD_NAME({coffer.TemplateId})|r][{coffer.ItemContainer.ContainerType}] {coffer.ItemContainer.Items.Count} entries");
                break;
            case "close":
                if (!DoodadManager.CloseCofferDoodad(null, coffer.ObjId))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Failed to close coffer {coffer.ObjId} ?");
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Closed Coffer {coffer.ObjId}");
                }

                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown sub-command {action}");
                break;
        }
    }
}
