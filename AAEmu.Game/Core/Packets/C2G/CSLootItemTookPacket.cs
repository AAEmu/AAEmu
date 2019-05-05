using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLootItemTookPacket : GamePacket
    {
        public CSLootItemTookPacket() : base(0x08f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var iid = stream.ReadUInt64();
            var count = stream.ReadInt32();

            Connection.ActiveChar.SendMessage("You get to item : " + iid + ":" + count);


            uint ObjId = (uint)(iid >> 32);
            if (CSLootOpenBagPacket.LootTempItems.ContainsKey(ObjId))
            {

                var ListItem = CSLootOpenBagPacket.LootTempItems[ObjId];
                if (ListItem == null)
                {
                    return;
                }
                var lootItem = ListItem.Find(a=>a.Id==iid);

                if (lootItem == null)
                {
                    return;
                }

                if (lootItem.TemplateId == 500)
                {
                    Connection.ActiveChar.Money += lootItem.Count;
                    Connection.ActiveChar.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillEffectGainItem,
                        new List<ItemTask> { new MoneyChange(lootItem.Count) }, new List<ulong>()));
                }
                else
                {
                    var item = ItemManager.Instance.Create(lootItem.TemplateId, count>lootItem.Count?lootItem.Count:count, lootItem.Grade);
                    InventoryHelper.AddItemAndUpdateClient(Connection.ActiveChar, item);
                }
                ListItem.Remove(lootItem);
                if (ListItem.Count <= 0)
                {
                    CSLootOpenBagPacket.LootTempItems.Remove(ObjId);
                    Connection.ActiveChar.BroadcastPacket(new SCLootableStatePacket(ObjId, false), true);
                }
                Connection.ActiveChar.SendPacket(new SCLootItemTookPacket(500, iid, count));

            }
            

            _log.Warn("LootItem, IId: {0}, Count: {1}", iid, count);
        }
    }
}
