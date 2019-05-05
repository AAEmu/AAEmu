using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLootOpenBagPacket : GamePacket
    {
        public CSLootOpenBagPacket() : base(0x08e, 1)
        {
        }

        public static Dictionary<uint, List<Item>> LootTempItems = new Dictionary<uint, List<Item>>();

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var obj2Id = stream.ReadBc();
            var lootAll = stream.ReadBoolean();
            
            List<Item> items;
            if (LootTempItems.ContainsKey(objId))
            {
                items = LootTempItems[objId];
            }
            else { 
            


                //获取npcId
                var unit = WorldManager.Instance.GetNpc(objId);

                if (unit == null)
                {
                    //todo 取消NPC的可捡取战利品
                    return;
                }

                List<LootPackDroppingNpc> lootPackDroppingNpcs = ItemManager.Instance.GetLootPackIdByNpcId(unit.TemplateId);

                if (lootPackDroppingNpcs.Count <= 0)
                {
                    //todo 取消NPC的可捡取战利品
                    return;
                }

                items = new List<Item>();

                ulong itemId = ((ulong)objId << 32) + 65536;

                foreach (var lootPackDroppingNpc in lootPackDroppingNpcs)
                {

                    LootPacks[] lootPacks = ItemManager.Instance.GetLootPacks(lootPackDroppingNpc.LootPackId);
                    var dropRateMax = (uint)0;
                    for (var ui = 0; ui < lootPacks.Length; ui++)
                    {
                            dropRateMax += lootPacks[ui].DropRate;
                    }

                    var dropRateItem = Rand.Next(0, dropRateMax);
                    var dropRateItemId = (uint)0;
                    for (var uii = 0; uii < lootPacks.Length; uii++)
                    {
                        //if (lootPacks[uii].Group == lootGroups[i].GroupNo)
                        //{
                        if (lootPacks[uii].DropRate + dropRateItemId >= dropRateItem)
                        {
                            //var template = ItemManager.Instance.GetTemplate(lootPacks[uii].ItemId);
                            Item item = new Item();
                            item.TemplateId = lootPacks[uii].ItemId;
                            item.WorldId = 1;
                            item.CreateTime = DateTime.Now;
                            item.Id = ++itemId;
                            item.MadeUnitId = objId;
                            item.Count = Rand.Next(lootPacks[uii].MinAmount, lootPacks[uii].MaxAmount);
                            items.Add(item);
                            Connection.ActiveChar.SendMessage("Test Loot item : " + lootPacks[uii].LootPackId + ":" + lootPacks[uii].ItemId);
                            break;
                        }
                        else
                        {
                            dropRateItemId += lootPacks[uii].DropRate;
                        }
                        //}
                    }

                    //foreach (var lootPack in lootPacks)
                    //{
                    //    var template = ItemManager.Instance.GetTemplate(lootPack.ItemId);
                    //    Item item = new Item();
                    //    item.TemplateId = lootPack.ItemId;
                    //    item.WorldId = 1;
                    //    item.CreateTime = DateTime.Now;
                    //    item.Id = objId;
                    //    item.MadeUnitId = objId;
                    //    items.Add(item);
                    //    Connection.ActiveChar.SendMessage("Test Loot item : " + lootPack.LootPackId + ":" + lootPack.ItemId);
                    //}
                }

                //var template2 = ItemManager.Instance.GetTemplate(500);
                Item item2 = new Item();
                item2.TemplateId = 500;
                item2.WorldId = 1;
                item2.CreateTime = DateTime.Now;
                item2.Id = ++itemId;
                item2.Count = Rand.Next(1, Connection.ActiveChar.Level* Connection.ActiveChar.Level * 10);
                item2.MadeUnitId = objId;
                items.Add(item2);
                Connection.ActiveChar.SendMessage("Test Loot item Gold coin:"+item2.Count);

                LootTempItems.Add(objId, items);

            }

            Connection.ActiveChar.SendPacket(new SCLootBagDataPacket(items, lootAll));

        }
    }
}
