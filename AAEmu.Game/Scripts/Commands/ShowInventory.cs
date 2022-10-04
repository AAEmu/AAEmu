using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Scripts.Commands
{
    public class ShowInventory : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "showinv", "show_inv", "showinventory", "show_inventory", "inventory", "invy" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) [containerId]";
        }

        public string GetCommandHelpText()
        {
            return "Show content of target's item container.\n"
                 + "Equipment = 1, Inventory = 2 (default), Bank = 3, Trade = 4, Mail = 5";
        }

        public void Execute(Character character, string[] args)
        {
            if (character.CurrentTarget is not Character && character.CurrentTarget is Unit unit)
            {
                var templateName = "Unit";
                foreach (var item in unit.Equipment.Items)
                {
                    if (unit is Npc npc)
                        templateName = $"|nc;@NPC_NAME({npc.TemplateId})|r";
                    var slotName = ((EquipmentItemSlot)item.Slot).ToString();
                    var countName = $"|ng;{item.Count.ToString()}|r x ";
                    if (item.Count == 1)
                        countName = string.Empty;
                    character.SendMessage("[{0}][{1}] {2}|nn;{3}|r = @ITEM_NAME({3})", templateName, slotName, countName, item.TemplateId);
                }

                character.SendMessage("[ShowInv] [{0}][{1}] {2} entries", templateName, unit.Equipment.ContainerType, unit.Equipment.Items.Count);
                return;
            }

            Character targetPlayer = character;
            var firstarg = 0;
            if (args.Length > 0)
                targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out firstarg);

            var containerId = SlotType.Inventory;
            if ((args.Length > firstarg) && (uint.TryParse(args[firstarg], out uint argcontainerId)))
            {
                if ((argcontainerId < 0 || argcontainerId > (byte)SlotType.Mail) && argcontainerId != (byte)SlotType.System)
                {
                    character.SendMessage("|cFFFF0000[ShowInv] Invalid container Id |r");
                    return;
                }
                containerId = (SlotType)argcontainerId;
            }

            var targetContainer = targetPlayer.Inventory.Bag;
            if (targetPlayer.Inventory._itemContainers.TryGetValue(containerId, out targetContainer))
            {
                foreach (var item in targetContainer.Items)
                {
                    var slotName = targetContainer.ContainerType.ToString() + "-" + item.Slot.ToString();
                    if (item.SlotType == SlotType.Equipment)
                        slotName = ((EquipmentItemSlot)item.Slot).ToString();
                    var countName = $"|ng;{item.Count.ToString()}|r x ";
                    if (item.Count == 1)
                        countName = string.Empty;
                    character.SendMessage("[|nd;{0}|r][{1}] |nb;{2}|r {3}|nn;{4}|r = @ITEM_NAME({4})",
                                            targetPlayer.Name, slotName,
                                            item.Id, countName, item.TemplateId
                                            );
                }

                character.SendMessage("[ShowInv][|nd;{0}|r][{1}] {2} entries", targetPlayer.Name, targetContainer.ContainerType, targetContainer.Items.Count);
                return;
            }

            character.SendMessage("|cFFFF0000[ShowInv] Unused container Id.|r");
        }
    }
}
