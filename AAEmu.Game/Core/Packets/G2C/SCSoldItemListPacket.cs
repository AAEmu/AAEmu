using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSoldItemListPacket : GamePacket
    {
        private Item[] _items;

        public SCSoldItemListPacket(Item[] items) : base(SCOffsets.SCSoldItemListPacket, 1)
        {
            _items = items;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_items.Length);
            foreach (var item in _items)
                stream.Write(item);
            return stream;
        }
    }
}
