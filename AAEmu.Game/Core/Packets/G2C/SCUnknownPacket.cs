using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnknownPacket : GamePacket
    {
        private readonly SlotType _slotType;
        private readonly byte _slot;
        private readonly SlotType _slotType2;
        private readonly byte _slot2;
        private readonly short _errorMessage;

        public SCUnknownPacket(SlotType slotType, byte slot, SlotType slotType2, byte slot2, short errorMessage) : base(SCOffsets.SCUnknownPacket, 5)
        {
            _slotType = slotType;
            _slot = slot;
            _slotType2 = slotType2;
            _slot2 = slot2;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_slotType);
            stream.Write(_slot);
            stream.Write((byte)_slotType2);
            stream.Write(_slot2);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}
