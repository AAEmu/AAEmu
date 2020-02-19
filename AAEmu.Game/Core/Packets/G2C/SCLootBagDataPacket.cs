using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCLootBagDataPacket : GamePacket
    {
        private readonly List<Item> _items;
        private readonly bool _lootAll;

        public SCLootBagDataPacket(List<Item> items, bool lootAll) : base(SCOffsets.SCLootBagDataPacket,1)
        {
            _items = items;
            _lootAll = lootAll;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_items.Count);

            foreach(var item in _items)
            {
                stream.Write(item.TemplateId);
                stream.Write(item.Id);
                stream.Write(item.Grade);
                stream.Write((byte)0);
                stream.Write(item.Count);
                stream.Write(item.DetailType);
                stream.Write(item.CreateTime);
                stream.Write(item.LifespanMins);
                stream.Write(item.MadeUnitId);
                stream.Write(item.WorldId);
                stream.Write(item.UnsecureTime);
                stream.Write(item.UnpackTime);
            }

            stream.Write(_lootAll);
            return stream;
        }
    }
}
