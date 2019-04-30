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
                item.Write(stream);
            }

            stream.Write(_lootAll);
            return stream;
        }
    }
}
