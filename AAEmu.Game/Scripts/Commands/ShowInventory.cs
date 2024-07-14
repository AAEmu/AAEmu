using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ShowInventory : ICommand
{
    public void OnLoad()
    {
        string[] name = { "showinv", "show_inv", "showinventory", "show_inventory", "inventory" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [containerId]";
    }

    public string GetCommandHelpText()
    {
        return "Show content of target's item container.\rEquipment = 1, Inventory = 2 (default), Bank = 3, Trade = 4, Mail = 5";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if ((!(character.CurrentTarget is Character)) && (character.CurrentTarget is Unit unit))
        {
            var targetContainer = unit.Equipment;
            var templateName = "Unit";
            foreach (var item in targetContainer.Items)
            {
                if (unit is Npc npc)
                    templateName = string.Format("|nc;@NPC_NAME({0})|r", npc.TemplateId);
                var slotName = ((EquipmentItemSlot)item.Slot).ToString();
                var countName = "|ng;" + item.Count.ToString() + "|r x ";
                if (item.Count == 1)
                    countName = string.Empty;
                character.SendMessage($"[{templateName}][{slotName}] {countName}|nn;{item.TemplateId}|r = @ITEM_NAME({item.TemplateId})");
            }
            character.SendMessage($"[ShowInv][{templateName}][{targetContainer.ContainerType}] {targetContainer.Items.Count} entries");
            return;
        }
        else
        {
            Character targetPlayer = character;
            var firstarg = 0;
            if (args.Length > 0)
                targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out firstarg);

            var containerId = SlotType.Inventory;

            if ((args.Length > firstarg + 0) && (uint.TryParse(args[firstarg + 0], out uint argcontainerId)))
            {
                if (((argcontainerId >= 0) && (argcontainerId <= (byte)SlotType.Mail)) || (argcontainerId == (byte)SlotType.System))
                    containerId = (SlotType)argcontainerId;
                else
                {
                    character.SendMessage("|cFFFF0000[ShowInv] Invalid container Id |r");
                    return;
                }
            }


            var targetContainer = targetPlayer.Inventory.Bag;
            if (targetPlayer.Inventory._itemContainers.TryGetValue(containerId, out targetContainer))
            {
                var showWarnings = (targetContainer.ContainerType == SlotType.Equipment) || (targetContainer.ContainerType == SlotType.Inventory) || (targetContainer.ContainerType == SlotType.Bank);
                var lastSlotNumber = -1;
                var hasSlotErrors = 0;
                foreach (var item in targetContainer.Items)
                {
                    var slotName = targetContainer.ContainerType.ToString() + "-" + item.Slot.ToString();
                    if (item.SlotType == SlotType.Equipment)
                        slotName = ((EquipmentItemSlot)item.Slot).ToString();
                    if (lastSlotNumber == item.Slot)
                    {
                        slotName = $"|cFFFF0000**{slotName}**|r";
                        hasSlotErrors++;
                    }
                    var countName = "|ng;" + item.Count.ToString() + "|r x ";
                    if (item.Count == 1)
                        countName = string.Empty;
                    character.SendMessage($"[|nd;{targetPlayer.Name}|r][{slotName}] |nb;{item.Id}|r {countName}|nn;{item.TemplateId}|r = @ITEM_NAME({item.TemplateId})");
                    lastSlotNumber = item.Slot;
                }

                if (hasSlotErrors > 0)
                    character.SendMessage($"[ShowInv][|nd;{targetPlayer.Name}|r] |cFFFF0000{targetContainer.ContainerType} contains {hasSlotErrors} slot number related errors, please manually fix these!|r");
                character.SendMessage($"[ShowInv][|nd;{targetPlayer.Name}|r][{targetContainer.ContainerType}] {targetContainer.Items.Count} entries");
            }
            else
            {
                character.SendMessage(ChatType.System, $"[ShowInv] Unused container Id.", Color.Red);
            }
        }

    }
}
