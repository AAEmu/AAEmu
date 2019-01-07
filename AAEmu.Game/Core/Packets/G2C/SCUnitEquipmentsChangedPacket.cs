using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitEquipmentsChangedPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly Tuple<byte, Item>[] _items;

        public SCUnitEquipmentsChangedPacket(uint characterId, Tuple<byte, Item>[] items) : base(0x08f, 1)
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
                stream.Write(item);
            }

            return stream;
        }
    }
}