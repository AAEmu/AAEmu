using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddItem : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("add_item", this);
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
                character.SendMessage("[Items] /add_item (target) <itemId> [count] [grade]");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            var itemId = 0;
            var count = 1;
            byte grade = 0;

            if ( (args.Length > firstarg + 0) && (uint.TryParse(args[firstarg + 0], out var argitemId)) )
                itemId = argitemId;

            if ((args.Length > firstarg + 1) && (int.TryParse(args[firstarg + 1], out var argcount)))
                count = argcount;

            if ((args.Length > firstarg + 2) && (byte.TryParse(args[firstarg + 2], out var arggrade)))
                grade = arggrade;

            var item = ItemManager.Instance.Create(itemId, count, grade, true);
            if (item == null)
            {
                character.SendMessage("|cFFFF0000Item could not be created!|r");
                return;
            }

            var res = character.Inventory.AddItem(item);
            if (res == null)
            {
                ItemIdManager.Instance.ReleaseId((uint) item.Id);
                return;
            }

            var tasks = new List<ItemTask>();
            if (res.Id != item.Id)
                tasks.Add(new ItemCountUpdate(res, item.Count));
            else
                tasks.Add(new ItemAdd(item));
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.AutoLootDoodadItem, tasks, new List<ulong>()));

        }
    }
}
