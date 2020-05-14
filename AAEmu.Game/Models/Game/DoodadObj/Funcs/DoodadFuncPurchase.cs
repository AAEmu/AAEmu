using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPurchase : DoodadFuncTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public uint CoinItemId { get; set; }
        public int CoinCount { get; set; }
        public uint CurrencyId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            Character character = (Character)caster;

            if (!character.Inventory.CheckItems(CoinItemId, CoinCount)) return;
            var tasksRemove = new List<ItemTask>();
            Item coinItem = character.Inventory.GetItemByTemplateId(CoinItemId);
            tasksRemove.Add(InventoryHelper.GetTaskAndRemoveItem(character, coinItem, CoinCount));

            var purchasedItem = ItemManager.Instance.Create(ItemId, Count, 0, true);
            var res = character.Inventory.AddItem(purchasedItem);
            var tasks = new List<ItemTask>();
            if (res.Id != purchasedItem.Id)
                tasks.Add(new ItemCountUpdate(res, purchasedItem.Count));
            else
                tasks.Add(new ItemAdd(purchasedItem));

            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.DoodadInteraction, tasksRemove, new List<ulong>()));
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy, tasks, new List<ulong>()));
        }
    }
}
