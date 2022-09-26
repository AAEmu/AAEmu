using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitEquipmentsChangedPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly (byte slot, Item item)[] _items;
        private readonly Item _item;

        public SCUnitEquipmentsChangedPacket(uint objectId, (byte slot, Item item)[] items) : base(SCOffsets.SCUnitEquipmentsChangedPacket, 5)
        {
            _characterId = objectId;
            _items = items;
            _item = new Item();
        }

        public SCUnitEquipmentsChangedPacket(uint objectId, byte slot, Item item) : base(SCOffsets.SCUnitEquipmentsChangedPacket, 5)
        {
            _characterId = objectId;
            _items = new[] { (slot, item) };
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_characterId);  // uid
            stream.Write((byte) _items.Length); // num
            foreach (var (slot, item) in _items) // 29 in 3+, 28 in 1.2
            {
                stream.Write(slot); // EquipSlot
                if (item == null)
                {
                    stream.Write(0);
                }
                else
                {
                    stream.Write(item);
                }
            }
            stream.Write(0u); // flags

            return stream;
        }
    }
}
