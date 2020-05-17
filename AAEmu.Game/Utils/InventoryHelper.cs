using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Utils
{
    public class InventoryHelper
    {
        public static bool AddItemAndUpdateClient(Character character, Item item)
        {
            return (character.Inventory.AddItem(ItemTaskType.Invalid, item) != null);
            /*
            var res = character.Inventory.AddItem(item);
            if (res == null)
            {
                ItemManager.Instance.ReleaseId(item.Id);
                return false;
            }

            var tasks = new List<ItemTask>();
            if (res.Id != item.Id)
                tasks.Add(new ItemCountUpdate(res, item.Count));
            else
                tasks.Add(new ItemAdd(item));
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.AutoLootDoodadItem, tasks,
                new List<ulong>()));
            return true;
            */
        }

        /*
        public static bool RemoveItemForMailing(Character character, Item item)
        {
            return character.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Mail, item);
        }

        public static bool RemoveItemAndUpdateClient(Character character, Item item, int count, bool releaseId = true)
        {
            if (item.Count > count)
            {
                item.Count -= count;
                character.SendPacket(
                    new SCItemTaskSuccessPacket(ItemTaskType.Destroy,
                        new List<ItemTask>
                        {
                            new ItemCountUpdate(item, -count)
                        }, new List<ulong>()));
                return true;
            }

            if (item.Count != count)
                return false;

            character.Inventory.RemoveItem(item, releaseId);
            character.SendPacket(
                new SCItemTaskSuccessPacket(ItemTaskType.Destroy,
                    new List<ItemTask>
                    {
                        new ItemRemove(item)
                    },
                    new List<ulong>()));
            return true;
        }

        public static ItemTask GetTaskAndRemoveItem(Character character, Item item, int count)
        {
            if (item.Count > count)
            {
                item.Count -= count;
                return new ItemCountUpdate(item, -count);
            }

            character.Inventory.RemoveItem(item, true);
            return new ItemRemove(item);
        }
        */
    }
}
