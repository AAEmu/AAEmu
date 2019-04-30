using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
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

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var obj2Id = stream.ReadBc();
            var lootAll = stream.ReadBoolean();
            var items = new List<Item>();


            Item item = ItemManager.Instance.Create(500, 5, 0);
            item.WorldId = 1;
            item.CreateTime = DateTime.Now;
            
            items.Add(item);

            Connection.ActiveChar.SendPacket(new SCLootBagDataPacket(items, lootAll));

        }
    }
}
