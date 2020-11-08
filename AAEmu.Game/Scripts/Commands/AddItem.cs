using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddItem : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "additem", "add_item" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <itemId> [count] [grade]";
        }

        public string GetCommandHelpText()
        {
            return "Adds item with template <itemId> and amount [count] at a specific [grade]. If [count] is omitted, the amount is one. If [grade] is ommited, the default defined for that item will be used.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Items] " + CommandManager.CommandPrefix + "additem (target) <itemId> [count] [grade]");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            uint itemId = 0;
            int count = 1;
            byte grade = 0;

            if ( (args.Length > firstarg + 0) && (uint.TryParse(args[firstarg + 0], out uint argitemId)) )
                itemId = argitemId;

            if ((args.Length > firstarg + 1) && (int.TryParse(args[firstarg + 1], out int argcount)))
                count = argcount;

            if ((args.Length > firstarg + 2) && (byte.TryParse(args[firstarg + 2], out byte arggrade)))
                grade = arggrade;

            if (grade > (byte)ItemGrade.Mythic || grade < (byte)ItemGrade.Crude)
            {
                character.SendMessage("|cFFFF0000Item grade cannot be lower than {0} or exceed {1}!|r", (byte)ItemGrade.Crude, (byte)ItemGrade.Mythic);
                return;
            }

            var itemTemplate = ItemManager.Instance.GetTemplate(itemId);
            if (itemTemplate == null)
            {
                character.SendMessage("|cFFFF0000Item template does not exist for {0} !|r", itemId);
                return;
            }

            if (itemTemplate.Category_Id == 133) // Speciality Packs (tradepacks) 
            {
                var currentBackpack = targetPlayer.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
                if (currentBackpack != null)
                {
                    character.SendMessage("|cFFFF0000No room on the backpack slot to place a tradepack!|r");
                    return;
                }
                if (!targetPlayer.Inventory.Equipment.AcquireDefaultItem(ItemTaskType.Gm, itemId, count, grade))
                {
                    character.SendMessage("|cFFFF0000Tradepack could not be created!|r");
                    return;
                }
            }
            else
            if (!targetPlayer.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Gm, itemId, count, grade))
            {
                character.SendMessage("|cFFFF0000Item could not be created!|r");
                return;
            }

            if (character.Id != targetPlayer.Id)
            {
                character.SendMessage("[Items] added item {0} to {1}'s inventory", itemId, targetPlayer.Name);
                targetPlayer.SendMessage("[GM] {0} has added a item to your inventory", character.Name);
            }

        }
    }
}
