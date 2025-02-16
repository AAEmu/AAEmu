using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellItemsPacket : GamePacket
{
    public CSSellItemsPacket() : base(CSOffsets.CSSellItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var npcObjId = stream.ReadBc();
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc?.Template.Merchant != true)
        {
            return;
        }

        var doodadObjId = stream.ReadBc();

        var num = stream.ReadByte();
        var items = new List<Item>();

        for (var i = 0; i < num; i++)
        {
            var slotType = (SlotType)stream.ReadByte();
            var slot = stream.ReadByte();

            var itemId = stream.ReadUInt64();
            var stack = stream.ReadUInt32();
            var removeReservationTime = stream.ReadDateTime();
            var type1 = stream.ReadUInt32();
            var dbSlaveId = stream.ReadUInt32();
            var type2 = stream.ReadUInt32();

            Item item = null;
            if (slotType == SlotType.Equipment)
            {
                item = Connection.ActiveChar.Inventory.Equipment.GetItemBySlot(slot);
            }
            else if (slotType == SlotType.Bag)
            {
                item = Connection.ActiveChar.Inventory.Bag.GetItemBySlot(slot);
            }

            //else if (slotType == SlotType.Bank)
            //    item = Connection.ActiveChar.Inventory.Bank[slot];
            if (item?.Id == itemId)
            {
                items.Add(item);
            }
        }

        //var tasks = new List<ItemTask>();
        var money = 0;
        foreach (var item in items)
        {
            if (!item.Template.Sellable)
            {
                continue;
            }

            if (!Connection.ActiveChar.BuyBackItems.AddOrMoveExistingItem(ItemTaskType.StoreSell, item))
            {
                Logger.Warn($"Failed to move sold itemId {item.Id} ({item.TemplateId}) to BuyBack ItemContainer for {Connection.ActiveChar.Name}");
            }
            money += (int)(item.Template.Refund * ItemManager.Instance.GetGradeTemplate(item.Grade).RefundMultiplier / 100f) * item.Count;
        }

        if (money == 0)
        {
            return;
        }
        Connection.ActiveChar.ChangeMoney(SlotType.Bag, money);

        //var itemTasks = new List<ItemTask>();
        //Connection.ActiveChar.Money += money;
        //itemTasks.Add(new MoneyChange(money));
        //Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreSell, itemTasks, new List<ulong>()));
    }
}
