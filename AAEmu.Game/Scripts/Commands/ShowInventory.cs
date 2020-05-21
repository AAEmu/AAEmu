using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
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

        public void Execute(Character character, string[] args)
        {
            Character targetPlayer = character;
            var firstarg = 0;
            if (args.Length > 0)
                targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out firstarg);

            var containerId = SlotType.Inventory ;

            if ((args.Length > firstarg + 0) && (uint.TryParse(args[firstarg + 0], out uint argcontainerId)))
            {
                if ((argcontainerId >= 0) && (argcontainerId <= (byte)SlotType.Mail))
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
                foreach (var item in targetContainer.Items)
                {
                    character.SendMessage("[|nd;{0}|r][{1}-{2}] |nb;{3}|r {4} x |nn;{5}|r = @ITEM_NAME({5})", 
                        targetPlayer.Name, targetContainer.ContainerType,item.Slot,
                        item.Id, item.Count, item.TemplateId
                        );
                }
                character.SendMessage("[ShowInv][|nb;{0}|r][{1}] {2} entries", targetPlayer.Name, targetContainer.ContainerType, targetContainer.Items.Count);
            }
            else
            {
                character.SendMessage("|cFFFF0000[ShowInv] Unused container Id.|r");
            }

        }
    }
}
