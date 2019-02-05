using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddItem : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("add_item", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Items] /add_item <itemId> <count?> <grade?>");
                return;
            }

            var itemId = uint.Parse(args[0]);
            var count = 1;
            byte grade = 0;
            if (args.Length > 1)
                count = int.Parse(args[1]);
            if (args.Length > 2)
                grade = byte.Parse(args[2]);
            var item = ItemManager.Instance.Create(itemId, count, grade, true);
            if (item == null)
            {
                character.SendMessage("Item cannot be created");
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