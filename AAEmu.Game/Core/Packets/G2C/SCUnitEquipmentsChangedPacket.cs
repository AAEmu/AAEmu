using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitEquipmentsChangedPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly (byte slot, Item item)[] _items;

        public SCUnitEquipmentsChangedPacket(uint characterId, (byte slot, Item item)[] items) : base(SCOffsets.SCUnitEquipmentsChangedPacket, 1)
        {
            _characterId = characterId;
            _items = items;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_characterId);
            stream.Write((byte) _items.Length); // TODO max 28
            foreach (var (slot, item) in _items)
            {
                stream.Write(slot);
                if (item == null)
                    stream.Write(0);
                else
                    stream.Write(item);
            }

            return stream;
        }
    }
}
