using System;
using System.Linq;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.World.Transform;
using System.Numerics;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class CofferActions : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "coffer", "chest" };
            CommandManager.Instance.Register(name, this);
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
                "If doodadObjId is ommited, the first found coffer in a 4m radius will be used.\n";
        }
        
        public void Execute(Character character, string[] args)
        {
            float checkRadius = 4f;
            var action = args.Length >= 1 ? args[0].ToLower() : "help";
            var doodadObjIdStr = args.Length >= 2 ? args[1] : "0";
            
            if (action == "help")
            {
                character.SendMessage("[Coffer] Usage: " + CommandManager.CommandPrefix + "coffer " + GetCommandLineHelp());
                return;
            }

            if (!uint.TryParse(doodadObjIdStr, out var doodadObjId))
            {
                character.SendMessage("|cFFFF0000[Coffer] Error parsing doodadObjId !|r");
                return;
            }

            DoodadCoffer coffer = null;
            if (doodadObjId <= 0)
            {
                var doodads = WorldManager.Instance.GetAround<DoodadCoffer>(character, checkRadius);
                if (doodads.Count <= 0)
                {
                    character.SendMessage("|cFFFF0000[Coffer] No coffers found nearby !|r");
                    return;
                }
                coffer = doodads.First();
            }
            else
            {
                var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
                if (doodad is DoodadCoffer dCoffer)
                    coffer = dCoffer;
                else
                {
                    character.SendMessage($"|cFFFF0000[Coffer] No coffers found with objId: {doodadObjId} !|r");
                    return;
                }
            }

            if (coffer == null)
            {
                character.SendMessage($"|cFFFF0000[Coffer] Not sure how we got here !?|r");
                return;
            }

            character.SendMessage($"[Coffer] objId: {coffer.ObjId}, DoodadDbId: {coffer.DbId}, ItemContainerDbId: {coffer.ItemContainer.ContainerId}, UsedBy: {coffer.OpenedBy?.Name ?? "<nobody>"}");
            
            switch (action)
            {
                case "view":
                    foreach (var item in coffer.ItemContainer.Items)
                    {
                        var slotName = item.Slot.ToString();
                        var countName = "|ng;" + item.Count.ToString() + "|r x ";
                        if (item.Count == 1)
                            countName = string.Empty;
                        character.SendMessage($"[|nd;@DOODAD_NAME({coffer.TemplateId})|r][{slotName}] |nb;{item.Id}|r {countName}|nn;{item.TemplateId}|r = @ITEM_NAME({item.TemplateId})");
                    }
                    character.SendMessage($"[ShowInv][|nd;@DOODAD_NAME({coffer.TemplateId})|r][{coffer.ItemContainer.ContainerType}] {coffer.ItemContainer.Items.Count} entries");
                    break;
                case "close":
                    if (!DoodadManager.Instance.CloseCofferDoodad(null, coffer.ObjId))
                        character.SendMessage($"|cFFFF0000[Coffer] Failed to close coffer {coffer.ObjId} ?|r");
                    else
                        character.SendMessage($"[Coffer] Closed Coffer {coffer.ObjId}");
                    break;
                default:
                    character.SendMessage($"|cFFFF0000[Coffer] Unknown sub-command {action}|r");
                    break;
            }
        }
    }
}
