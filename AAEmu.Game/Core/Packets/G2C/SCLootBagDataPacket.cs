using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCLootBagDataPacket : GamePacket
    {
        private readonly byte _count;
        private readonly Item _item;
        private readonly bool _lootAll;

        public SCLootBagDataPacket(byte count,Item item, bool lootAll) : base(SCOffsets.SCLootBagDataPacket,1)
        {
            _count = count;
            _item = item;
            _lootAll = lootAll;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_count);
            if (_count > 0)
            {
                _item.Write(stream);
            }
            
            stream.Write(_lootAll);
            return stream;
        }
    }
}
