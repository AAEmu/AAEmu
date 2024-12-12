using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ShowInventory : ICommand
{
    public string[] CommandNames { get; set; } = ["inventory", "showinv", "show_inv", "showinventory", "show_inventory"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [containerId] [fix]";
    }

    public string GetCommandHelpText()
    {
        return
            "Show content of target's item container.\rEquipment = 1, Inventory = 2 (default), Bank = 3, Trade = 4, Mail = 5";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget is not Character && character.CurrentTarget is Unit unit)
        {
            var targetContainer = unit.Equipment;
            var templateName = "Unit";
            foreach (var item in targetContainer.Items.OrderBy(x => x.Slot).ThenBy(x => x.CreateTime).ToList())
            {
                if (unit is Npc npc)
                {
                    templateName = string.Format("|nc;@NPC_NAME({0})|r", npc.TemplateId);
                }

                var slotName = ((EquipmentItemSlot)item.Slot).ToString();
                var countName = "|ng;" + item.Count.ToString() + "|r x ";
                if (item.Count == 1)
                {
                    countName = string.Empty;
                }

                messageOutput.SendMessage(
                    $"[{templateName}][{slotName}] {countName}|nn;{item.TemplateId}|r = @ITEM_NAME({item.TemplateId})");
            }

            CommandManager.SendNormalText(this, messageOutput,
                $"[{templateName}][{targetContainer.ContainerType}] {targetContainer.Items.Count} entries");
            return;
        }
        else
        {
            var targetPlayer = character;
            var firstarg = 0;
            if (args.Length > 0)
            {
                targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out firstarg);
            }

            var containerId = SlotType.Inventory;

            if (args.Length > firstarg + 0 && Enum.TryParse<SlotType>(args[firstarg + 0], true, out var argContainerId))
            {
                if (argContainerId <= SlotType.Mail || argContainerId == SlotType.System)
                {
                    containerId = argContainerId;
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Invalid ContainerType Id {argContainerId}");
                    return;
                }
            }

            var doTryFix = args.Length > firstarg + 1 && args[firstarg + 1].Equals("fix", StringComparison.InvariantCultureIgnoreCase);
            var invalidItems = new List<Item>();

            if (targetPlayer.Inventory._itemContainers.TryGetValue(containerId, out var targetContainer))
            {
                var showWarnings = targetContainer.ContainerType == SlotType.Equipment ||
                                   targetContainer.ContainerType == SlotType.EquipmentMate ||
                                   targetContainer.ContainerType == SlotType.Inventory ||
                                   targetContainer.ContainerType == SlotType.Bank;
                var lastSlotNumber = -1;
                var hasSlotErrors = 0;
                foreach (var item in targetContainer.Items.OrderBy(x => x.Slot).ThenBy(x => x.CreateTime).ToList())
                {
                    var additionalErrors = string.Empty;
                    var slotName = targetContainer.ContainerType.ToString() + "-" + item.Slot.ToString();
                    if (item.SlotType == SlotType.Equipment)
                    {
                        slotName = ((EquipmentItemSlot)item.Slot).ToString();
                    }

                    if (lastSlotNumber == item.Slot && showWarnings)
                    {
                        slotName = $"|cFFFF0000**{slotName}**|r";
                        invalidItems.Add(item);
                        hasSlotErrors++;
                    }
                    else if (item.SlotType != item._holdingContainer?.ContainerType)
                    {
                        additionalErrors += "|cFFFF0000**Container Error**|r";
                        invalidItems.Add(item);
                        hasSlotErrors++;
                    }

                    var countName = $"|ng;{item.Count}|r x ";
                    if (item.Count == 1)
                    {
                        countName = string.Empty;
                    }

                    messageOutput.SendMessage($"[|nd;{targetPlayer.Name}|r][{slotName}] |nb;{item.Id}|r {countName}|nn;{item.TemplateId}|r = @ITEM_NAME({item.TemplateId}){additionalErrors}");
                    lastSlotNumber = item.Slot;
                }

                if (hasSlotErrors > 0)
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"[|nd;{targetPlayer.Name}|r] |cFFFF0000{targetContainer.ContainerType} contains {hasSlotErrors} slot number related errors, please manually fix these!|r");
                }

                CommandManager.SendNormalText(this, messageOutput,
                    $"[|nd;{targetPlayer.Name}|r][{targetContainer.ContainerType}] {targetContainer.Items.Count} entries");
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput, $"Unused container Id {containerId}");
            }

            if (doTryFix && invalidItems.Count > 0 && targetContainer != null)
            {
                var fixedCount = 0;
                switch (targetContainer.ContainerType)
                {
                    case SlotType.Equipment:
                    case SlotType.EquipmentMate:
                        // Fix equipment by moving it to inventory
                        foreach (var invalidItem in invalidItems)
                        {
                            var nextSlot = targetContainer.Owner.Inventory.Bag.GetUnusedSlot(-1);
                            if ((nextSlot < 0) || !targetContainer.Owner.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, invalidItem))
                            {
                                CommandManager.SendErrorText(this, messageOutput, $"Unable to fix {invalidItem.TemplateId} ({invalidItem.TemplateId}) ItemId: {invalidItem.Id}, no more room in your inventory to move this item!");
                                continue;
                            }
                            invalidItem.Slot = nextSlot;
                            invalidItem._holdingContainer = targetContainer.Owner.Inventory.Bag;
                            fixedCount++;
                        }
                        break;
                    case SlotType.Inventory:
                    case SlotType.Bank:
                        // Fix things
                        foreach (var invalidItem in invalidItems)
                        {
                            var nextSlot = targetContainer.GetUnusedSlot(-1);
                            if (nextSlot < 0)
                            {
                                CommandManager.SendErrorText(this, messageOutput, $"Unable to fix {invalidItem.TemplateId} ({invalidItem.TemplateId}) ItemId: {invalidItem.Id}, no more room!");
                                continue;
                            }
                            invalidItem.Slot = nextSlot;
                            fixedCount++;
                        }
                        break;
                    default:
                        // Can't or doesn't need fixing
                        break;
                }

                if (fixedCount > 0)
                {
                    CommandManager.SendNormalText(this, messageOutput, $"{fixedCount} items have been re-slotted, fully |ni;reboot your game client|r for the fixes to take affect");
                }
            }
        }
    }
}
