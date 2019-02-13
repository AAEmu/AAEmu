using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInvenExpandedPacket : GamePacket
    {
        private readonly byte _slotType;
        private readonly byte _numSlots;

        public SCInvenExpandedPacket(SlotType slotType, byte numSlots) : base(0x04c, 1) // TODO 1.0 opcode: 0x048
        {
            _slotType = (byte)slotType;
            _numSlots = numSlots;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_slotType);
            stream.Write(_numSlots);
            return stream;
        }
    }
}
